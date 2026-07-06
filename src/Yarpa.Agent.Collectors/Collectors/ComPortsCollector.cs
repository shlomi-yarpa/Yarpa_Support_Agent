using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects all COM (serial) ports present on the machine and their associated device names.
/// Primary source: Registry HARDWARE\DEVICEMAP\SERIALCOMM.
/// Supplemented by WMI Win32_PnPEntity (class = Ports) for device names.
/// </summary>
public sealed class ComPortsCollector : ICollector
{
    public string SectionName => "comPorts";

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

    private static List<ComPortInfo> Collect()
    {
        // Registry is the canonical source for active COM ports
        var portNames = ReadPortsFromRegistry();

        // Build a COM port → friendly device name map from WMI
        var deviceNameMap = BuildDeviceNameMap();

        var ports = new List<ComPortInfo>();
        foreach (string port in portNames.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            deviceNameMap.TryGetValue(port, out string? deviceName);
            ports.Add(new ComPortInfo
            {
                Port = port,
                DeviceName = deviceName
            });
        }

        return ports;
    }

    private static List<string> ReadPortsFromRegistry()
    {
        var ports = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key == null) return ports;

            foreach (string valueName in key.GetValueNames())
            {
                string? portName = key.GetValue(valueName)?.ToString();
                if (!string.IsNullOrEmpty(portName))
                    ports.Add(portName);
            }
        }
        catch { /* non-fatal */ }

        return ports;
    }

    /// <summary>
    /// Returns a map of "COM3" → "USB Serial Device (COM3)" from WMI Ports class.
    /// </summary>
    private static Dictionary<string, string> BuildDeviceNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Ports'");

            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;

                // Extract "(COMx)" from the device name, e.g. "USB Serial Device (COM3)"
                int start = name.LastIndexOf('(');
                int end = name.LastIndexOf(')');
                if (start >= 0 && end > start)
                {
                    string port = name.Substring(start + 1, end - start - 1).Trim();
                    if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        map[port] = name;
                }
            }
        }
        catch { /* non-fatal */ }

        return map;
    }
}
