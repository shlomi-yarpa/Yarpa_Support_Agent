using System.Management;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Yarpa.Agent;

/// <summary>
/// Computes a stable, unique identifier for the local machine.
/// Priority: HKLM MachineGuid → fallback hash of BIOS serial + primary MAC.
/// The result is cached for the lifetime of the instance (singleton in DI).
/// </summary>
public sealed class MachineIdentity
{
    private readonly string _machineId;

    public MachineIdentity()
    {
        _machineId = Compute();
    }

    public string MachineId => _machineId;

    private static string Compute()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography");
            if (key != null)
            {
                string? guid = key.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrWhiteSpace(guid))
                    return guid.ToLowerInvariant().Replace("-", "");
            }
        }
        catch { /* fall through to fallback */ }

        return ComputeFallback();
    }

    private static string ComputeFallback()
    {
        string biosSerial = ReadBiosSerial();
        string mac = ReadFirstMac();
        string combined = $"{biosSerial}|{mac}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ReadBiosSerial()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
                return obj["SerialNumber"]?.ToString()?.Trim() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private static string ReadFirstMac()
    {
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && nic.OperationalStatus == OperationalStatus.Up)
                {
                    string mac = nic.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrEmpty(mac))
                        return mac;
                }
            }
        }
        catch { }
        return string.Empty;
    }
}
