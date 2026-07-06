using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects USB devices currently connected to the machine.
/// Source: WMI Win32_PnPEntity filtered to USB devices; VID/PID extracted from DeviceID.
/// </summary>
public sealed class UsbDevicesCollector : ICollector
{
    public string SectionName => "usbDevices";

    // Matches "USB\VID_XXXX&PID_XXXX" in a PnP DeviceID string
    private static readonly Regex VidPidPattern =
        new(@"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var data = await Task.Run(() => Collect(), ct);
            sw.Stop();
            return CollectorResult.Ok(SectionName, data, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CollectorResult.Failed(SectionName, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static List<UsbDeviceInfo> Collect()
    {
        var devices = new List<UsbDeviceInfo>();

        // WMI query: PnP entities whose DeviceID starts with USB\ have VID/PID in the ID.
        // We filter on ConfigManagerErrorCode = 0 (device working properly) to skip phantom devices,
        // but we don't require it strictly – some devices report non-zero even when connected.
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, Manufacturer, PNPClass " +
            "FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\%'");

        foreach (ManagementObject obj in searcher.Get())
        {
            string deviceId = obj["DeviceID"]?.ToString() ?? string.Empty;
            Match m = VidPidPattern.Match(deviceId);

            string? vid = m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
            string? pid = m.Success ? m.Groups[2].Value.ToUpperInvariant() : null;

            string name = obj["Name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;

            devices.Add(new UsbDeviceInfo
            {
                Name = name,
                Vid = vid,
                Pid = pid,
                DeviceClass = obj["PNPClass"]?.ToString(),
                Manufacturer = obj["Manufacturer"]?.ToString()
            });
        }

        return devices;
    }
}
