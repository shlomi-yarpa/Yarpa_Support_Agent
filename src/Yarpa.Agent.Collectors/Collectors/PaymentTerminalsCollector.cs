using System.Diagnostics;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Identifies payment terminal devices by cross-referencing USB VID/PID with a
/// configurable vendor mapping table (config/payment-terminal-vendors.json).
/// COM port assignment is determined by checking registered serial ports.
/// </summary>
public sealed class PaymentTerminalsCollector : ICollector
{
    private readonly string _vendorConfigPath;

    private static readonly Regex VidPidPattern =
        new(@"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="vendorConfigPath">
    /// Absolute path to payment-terminal-vendors.json.
    /// Defaults to config/payment-terminal-vendors.json next to the executable.
    /// </param>
    public PaymentTerminalsCollector(string? vendorConfigPath = null)
    {
        _vendorConfigPath = vendorConfigPath
            ?? Path.Combine(AppContext.BaseDirectory, "config", "payment-terminal-vendors.json");
    }

    public string SectionName => "paymentTerminals";

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (terminals, partialError) = await Task.Run(() => Collect(_vendorConfigPath), ct);
            sw.Stop();

            if (partialError != null)
                return CollectorResult.Partial(SectionName, terminals, partialError, sw.ElapsedMilliseconds);

            return CollectorResult.Ok(SectionName, terminals, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CollectorResult.Failed(SectionName, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static (List<PaymentTerminalInfo> Terminals, string? PartialError) Collect(string vendorConfigPath)
    {
        // Load vendor mapping
        var (vendorMap, configError) = LoadVendorMap(vendorConfigPath);

        // Collect USB devices and COM ports in parallel internally
        var usbDevices = QueryUsbDevices();
        var comPortMap = QueryComPortDeviceMap();

        var terminals = new List<PaymentTerminalInfo>();

        foreach (var (name, vid, pid, deviceId) in usbDevices)
        {
            if (vid == null) continue;

            // Try exact VID+PID match first, then VID-only wildcard
            VendorEntry? entry = FindEntry(vendorMap, vid, pid);
            if (entry == null) continue;

            // Try to find a COM port whose WMI device name contains part of this USB device's name
            string? comPort = FindComPort(deviceId, comPortMap);

            terminals.Add(new PaymentTerminalInfo
            {
                Vendor = entry.Vendor,
                Model = entry.Model,
                ComPort = comPort,
                Vid = vid,
                Pid = pid ?? string.Empty
            });
        }

        return (terminals, configError);
    }

    private static VendorEntry? FindEntry(List<VendorEntry> map, string vid, string? pid)
    {
        // Exact match (VID + PID)
        if (!string.IsNullOrEmpty(pid))
        {
            var exact = map.FirstOrDefault(e =>
                string.Equals(e.Vid, vid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Pid, pid, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        // Wildcard match (VID only, no PID restriction in config)
        return map.FirstOrDefault(e =>
            string.Equals(e.Vid, vid, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(e.Pid));
    }

    /// <summary>
    /// Returns a list of (name, vid, pid, deviceId) tuples for all USB PnP entities.
    /// </summary>
    private static List<(string Name, string? Vid, string? Pid, string DeviceId)> QueryUsbDevices()
    {
        var result = new List<(string, string?, string?, string)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                string deviceId = obj["DeviceID"]?.ToString() ?? string.Empty;
                string name = obj["Name"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                Match m = VidPidPattern.Match(deviceId);
                string? vid = m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
                string? pid = m.Success ? m.Groups[2].Value.ToUpperInvariant() : null;

                result.Add((name, vid, pid, deviceId));
            }
        }
        catch { /* non-fatal */ }

        return result;
    }

    /// <summary>
    /// Returns a map from device name fragment → COM port string.
    /// Key: friendly name from WMI Ports class. Value: "COM3".
    /// </summary>
    private static List<(string DeviceName, string ComPort)> QueryComPortDeviceMap()
    {
        var result = new List<(string, string)>();
        try
        {
            // Registry: HKLM\HARDWARE\DEVICEMAP\SERIALCOMM (fastest, most reliable)
            using var regKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (regKey != null)
            {
                foreach (string valueName in regKey.GetValueNames())
                {
                    string? portName = regKey.GetValue(valueName)?.ToString();
                    if (!string.IsNullOrEmpty(portName))
                        result.Add((valueName, portName));
                }
            }
        }
        catch { /* non-fatal */ }

        // Also get WMI Ports friendly names
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Ports'");

            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? string.Empty;
                int start = name.LastIndexOf('(');
                int end = name.LastIndexOf(')');
                if (start >= 0 && end > start)
                {
                    string port = name.Substring(start + 1, end - start - 1).Trim();
                    if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        result.Add((name, port));
                }
            }
        }
        catch { /* non-fatal */ }

        return result;
    }

    /// <summary>
    /// Resolves the COM port assigned to a specific USB device. The authoritative source is the
    /// device's own registry key: HKLM\SYSTEM\CurrentControlSet\Enum\&lt;deviceId&gt;\Device Parameters
    /// value "PortName" (e.g. "COM3"). This is exact per-device, unlike name-based heuristics.
    /// Falls back to a single unambiguous USB serial port from the map only when exactly one exists.
    /// </summary>
    private static string? FindComPort(string deviceId, List<(string DeviceName, string ComPort)> comPortMap)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            try
            {
                string subKey = $@"SYSTEM\CurrentControlSet\Enum\{deviceId}\Device Parameters";
                using var key = Registry.LocalMachine.OpenSubKey(subKey);
                string? portName = key?.GetValue("PortName")?.ToString();
                if (!string.IsNullOrEmpty(portName) &&
                    portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                    return portName;
            }
            catch { /* fall through to the heuristic fallback */ }
        }

        // Fallback: only associate when there is exactly one USB/serial COM port on the machine,
        // so we never attribute an arbitrary port to the wrong terminal.
        var usbPorts = comPortMap
            .Where(p => p.DeviceName.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                        p.DeviceName.Contains("Serial", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ComPort)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return usbPorts.Count == 1 ? usbPorts[0] : null;
    }

    // ── Vendor map loading ────────────────────────────────────────────────────

    private static (List<VendorEntry> Map, string? Error) LoadVendorMap(string configPath)
    {
        if (!File.Exists(configPath))
            return (new List<VendorEntry>(), $"vendor config not found at {configPath}");

        try
        {
            string json = File.ReadAllText(configPath);
            var doc = JsonSerializer.Deserialize<VendorConfigDocument>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (doc?.Vendors == null)
                return (new List<VendorEntry>(), "vendor config has no vendors array");

            var flat = new List<VendorEntry>();
            foreach (var v in doc.Vendors)
            {
                if (v.Entries == null) continue;
                foreach (var e in v.Entries)
                {
                    flat.Add(new VendorEntry
                    {
                        Vendor = v.Vendor ?? string.Empty,
                        Vid = e.Vid ?? string.Empty,
                        Pid = string.IsNullOrWhiteSpace(e.Pid) ? null : e.Pid,
                        Model = string.IsNullOrWhiteSpace(e.Model) ? null : e.Model
                    });
                }
            }

            return (flat, null);
        }
        catch (Exception ex)
        {
            return (new List<VendorEntry>(), $"failed to parse vendor config: {ex.Message}");
        }
    }

    // ── Private config model (JSON deserialization only) ─────────────────────

    private sealed class VendorConfigDocument
    {
        [JsonPropertyName("vendors")]
        public List<VendorGroup>? Vendors { get; set; }
    }

    private sealed class VendorGroup
    {
        [JsonPropertyName("vendor")]
        public string? Vendor { get; set; }

        [JsonPropertyName("entries")]
        public List<VendorEntryRaw>? Entries { get; set; }
    }

    private sealed class VendorEntryRaw
    {
        [JsonPropertyName("vid")]
        public string? Vid { get; set; }

        [JsonPropertyName("pid")]
        public string? Pid { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }
    }

    private sealed class VendorEntry
    {
        public string Vendor { get; init; } = string.Empty;
        public string Vid { get; init; } = string.Empty;
        public string? Pid { get; init; }
        public string? Model { get; init; }
    }
}
