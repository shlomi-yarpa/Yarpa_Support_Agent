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
    /// Returns a map of drive letter → "SSD" or "HDD" using the Storage namespace.
    /// Returns null when the namespace is unavailable (e.g. no admin rights / older OS).
    /// </summary>
    private static Dictionary<string, string>? BuildMediaTypeMap()
    {
        try
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // MediaType: 3 = HDD, 4 = SSD, 5 = SCM (treat as SSD)
            const string query = "SELECT DriveLetter, MediaType FROM MSFT_PhysicalDisk";
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
            foreach (ManagementObject obj in searcher.Get())
            {
                string driveLetter = obj["DriveLetter"]?.ToString()?.Trim() ?? string.Empty;
                uint mediaTypeValue = Convert.ToUInt32(obj["MediaType"] ?? 0u);

                if (string.IsNullOrEmpty(driveLetter)) continue;

                string type = mediaTypeValue switch
                {
                    3 => "HDD",
                    4 or 5 => "SSD",
                    _ => "Unknown"
                };

                map[driveLetter] = type;
            }

            return map;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveMediaType(string drive, Dictionary<string, string> map)
    {
        // drive is like "C:" – try with and without colon
        if (map.TryGetValue(drive, out string? type)) return type;
        if (drive.Length >= 1 && map.TryGetValue(drive[0].ToString(), out type)) return type;
        return null;
    }
}
