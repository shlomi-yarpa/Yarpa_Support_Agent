using System.Diagnostics;
using System.Management;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects logical disk information: letter, size, free space and media type.
/// Primary source: WMI Win32_LogicalDisk (DriveType=3 = local fixed disk).
/// Media type (SSD/HDD) is queried from MSFT_PhysicalDisk where available;
/// it is omitted rather than failing the whole section when that query is not accessible.
/// </summary>
public sealed class DiskCollector : ICollector
{
    public string SectionName => "disks";

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (disks, partialError) = await Task.Run(() => Collect(), ct);
            sw.Stop();

            if (partialError != null)
                return CollectorResult.Partial(SectionName, disks, partialError, sw.ElapsedMilliseconds);

            return CollectorResult.Ok(SectionName, disks, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CollectorResult.Failed(SectionName, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static (List<DiskInfo> Disks, string? PartialError) Collect()
    {
        // Build a VolumeId → media type map via MSFT_PhysicalDisk (Storage namespace).
        // This query can fail silently (older OS / permission issues) – we carry on without it.
        var mediaTypeMap = BuildMediaTypeMap();
        string? partialError = mediaTypeMap == null
            ? "media type unavailable (MSFT_PhysicalDisk query failed)"
            : null;

        var disks = new List<DiskInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");

        foreach (ManagementObject obj in searcher.Get())
        {
            string drive = obj["DeviceID"]?.ToString() ?? string.Empty;
            long sizeBytes = Convert.ToInt64(obj["Size"] ?? 0L);
            long freeBytes = Convert.ToInt64(obj["FreeSpace"] ?? 0L);

            double sizeGb = Math.Round(sizeBytes / 1_073_741_824.0, 1);
            double freeGb = Math.Round(freeBytes / 1_073_741_824.0, 1);
            double freePercent = sizeBytes > 0
                ? Math.Round(freeBytes * 100.0 / sizeBytes, 1)
                : 0;

            string? mediaType = mediaTypeMap != null
                ? ResolveMediaType(drive, mediaTypeMap)
                : null;

            disks.Add(new DiskInfo
            {
                Drive = drive,
                SizeGb = sizeGb,
                FreeGb = freeGb,
                FreePercent = freePercent,
                MediaType = mediaType
            });
        }

        return (disks, partialError);
    }

    /// <summary>
    /// Returns a map of drive letter ("C:") → "SSD" / "HDD" / "Unknown".
    /// Combines two sources that do not require administrator rights:
    ///   • MSFT_PhysicalDisk (ROOT\Microsoft\Windows\Storage): physical disk number → media type.
    ///     MSFT_PhysicalDisk has no DriveLetter column (the previous cause of failure), so it is
    ///     keyed by its numeric DeviceId, with SpindleSpeed used as a fallback signal.
    ///   • Win32 association chain (CIMV2): drive letter → partition → physical disk number.
    /// Returns null only when the Storage namespace is genuinely inaccessible (permissions /
    /// older OS), in which case the caller reports the section as partial.
    /// </summary>
    private static Dictionary<string, string>? BuildMediaTypeMap()
    {
        Dictionary<int, string> indexToMediaType;
        try
        {
            indexToMediaType = QueryPhysicalDiskMediaTypes();
        }
        catch
        {
            // Storage namespace unavailable → signal partial to the caller.
            return null;
        }

        // Map every fixed logical drive letter to its physical disk index. This uses the
        // standard CIMV2 associators and is best-effort: failures here just mean some drives
        // resolve to Unknown rather than failing the whole section.
        // Only record letters we can classify definitively (SSD/HDD). Undetermined disks are
        // left absent → their mediaType is reported as null, and the section stays Ok (not partial).
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (driveLetter, diskIndex) in MapDriveLetterToDiskIndex())
        {
            if (indexToMediaType.TryGetValue(diskIndex, out string? t))
                map[driveLetter] = t;
        }

        return map;
    }

    /// <summary>
    /// Reads MSFT_PhysicalDisk and returns physical-disk-number → media type.
    /// MediaType: 3 = HDD, 4 = SSD, 5 = SCM (treated as SSD). When MediaType is Unspecified,
    /// a SpindleSpeed of 0 indicates a solid-state device.
    /// </summary>
    private static Dictionary<int, string> QueryPhysicalDiskMediaTypes()
    {
        var result = new Dictionary<int, string>();

        var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("SELECT DeviceId, MediaType, SpindleSpeed FROM MSFT_PhysicalDisk"));

        foreach (ManagementObject obj in searcher.Get())
        {
            string deviceId = obj["DeviceId"]?.ToString()?.Trim() ?? string.Empty;
            if (!int.TryParse(deviceId, out int index)) continue;

            uint mediaType = obj["MediaType"] != null ? Convert.ToUInt32(obj["MediaType"]) : 0u;

            uint? spindle = null;
            if (obj["SpindleSpeed"] != null)
            {
                try { spindle = Convert.ToUInt32(obj["SpindleSpeed"]); }
                catch { spindle = null; }
            }

            string? type = mediaType switch
            {
                3 => "HDD",
                4 or 5 => "SSD",
                // MediaType Unspecified: a reported SpindleSpeed of 0 indicates solid-state.
                // A positive spindle speed indicates a rotational HDD. Otherwise leave undetermined.
                _ => spindle == 0 ? "SSD" : spindle > 0 ? "HDD" : null
            };

            if (type != null)
                result[index] = type;
        }

        return result;
    }

    /// <summary>
    /// Maps each fixed logical drive letter ("C:") to its physical disk index via the
    /// Win32_LogicalDisk → Win32_DiskPartition association (DiskPartition.DiskIndex).
    /// </summary>
    private static List<(string DriveLetter, int DiskIndex)> MapDriveLetterToDiskIndex()
    {
        var result = new List<(string, int)>();
        try
        {
            using var logicalDisks = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_LogicalDisk WHERE DriveType=3");

            foreach (ManagementObject ld in logicalDisks.Get())
            {
                string letter = ld["DeviceID"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(letter)) continue;

                try
                {
                    string assoc =
                        $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}'}} " +
                        "WHERE AssocClass=Win32_LogicalDiskToPartition";

                    using var partitions = new ManagementObjectSearcher(new ObjectQuery(assoc));
                    foreach (ManagementObject part in partitions.Get())
                    {
                        if (part["DiskIndex"] != null &&
                            int.TryParse(part["DiskIndex"].ToString(), out int idx))
                        {
                            result.Add((letter, idx));
                            break;
                        }
                    }
                }
                catch { /* skip this drive; it will resolve to Unknown */ }
            }
        }
        catch { /* best-effort: return whatever mapped */ }

        return result;
    }

    private static string? ResolveMediaType(string drive, Dictionary<string, string> map)
    {
        // drive is like "C:" – try with and without colon
        if (map.TryGetValue(drive, out string? type)) return type;
        if (drive.Length >= 1 && map.TryGetValue(drive[0].ToString(), out type)) return type;
        return null;
    }
}
