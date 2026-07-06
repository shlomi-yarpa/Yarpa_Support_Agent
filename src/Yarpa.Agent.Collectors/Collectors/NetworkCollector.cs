using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects active network adapters: name, MAC, IPv4, gateway and DNS servers.
/// External IP is skipped (requires outbound HTTP; optional per spec).
/// Source: System.Net.NetworkInformation supplemented by WMI Win32_NetworkAdapterConfiguration.
/// </summary>
public sealed class NetworkCollector : ICollector
{
    public string SectionName => "network";

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

    private static NetworkData Collect()
    {
        // Build a MAC → gateway+DNS map from WMI (best-effort)
        var wmiMap = BuildWmiMap();

        var adapters = new List<NetworkAdapterInfo>();

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Skip loopback, tunnel and disconnected interfaces
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                                         or NetworkInterfaceType.Tunnel)
                continue;

            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            string mac = FormatMac(nic.GetPhysicalAddress().ToString());
            if (string.IsNullOrEmpty(mac) || mac == "000000000000")
                continue;

            string? ipv4 = null;
            foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ipv4 = addr.Address.ToString();
                    break;
                }
            }

            string? gateway = null;
            var dns = new List<string>();

            if (wmiMap.TryGetValue(mac, out var wmiEntry))
            {
                gateway = wmiEntry.Gateway;
                dns = wmiEntry.Dns;
            }
            else
            {
                // Fallback: read from NetworkInterface properties
                foreach (GatewayIPAddressInformation gw in nic.GetIPProperties().GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        gateway = gw.Address.ToString();
                        break;
                    }
                }

                foreach (System.Net.IPAddress dnsAddr in nic.GetIPProperties().DnsAddresses)
                {
                    if (dnsAddr.AddressFamily == AddressFamily.InterNetwork)
                        dns.Add(dnsAddr.ToString());
                }
            }

            adapters.Add(new NetworkAdapterInfo
            {
                Name = nic.Name,
                Mac = mac,
                Ipv4 = ipv4,
                Gateway = gateway,
                Dns = dns
            });
        }

        return new NetworkData { Adapters = adapters };
    }

    /// <summary>
    /// Reads gateway and DNS from WMI Win32_NetworkAdapterConfiguration for adapters
    /// with IP enabled. Returns a map keyed by formatted MAC address.
    /// </summary>
    private static Dictionary<string, (string? Gateway, List<string> Dns)> BuildWmiMap()
    {
        var map = new Dictionary<string, (string? Gateway, List<string> Dns)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT MACAddress, DefaultIPGateway, DNSServerSearchOrder " +
                "FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                string rawMac = obj["MACAddress"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(rawMac)) continue;

                string mac = rawMac.Replace(":", string.Empty).Replace("-", string.Empty).ToUpperInvariant();

                string? gateway = null;
                if (obj["DefaultIPGateway"] is string[] gateways && gateways.Length > 0)
                    gateway = gateways[0];

                var dns = new List<string>();
                if (obj["DNSServerSearchOrder"] is string[] servers)
                    dns.AddRange(servers);

                map[mac] = (gateway, dns);
            }
        }
        catch { /* non-fatal; caller will use NetworkInterface fallback */ }

        return map;
    }

    private static string FormatMac(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length < 12)
            return string.Empty;

        // Insert colons every two characters
        return string.Join(":", Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)));
    }
}
