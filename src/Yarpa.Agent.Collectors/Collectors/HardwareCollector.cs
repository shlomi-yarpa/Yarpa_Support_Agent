using System.Diagnostics;
using System.Management;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects hardware identification: manufacturer, model, serial number, BIOS,
/// CPU and RAM details. All sourced from WMI.
/// </summary>
public sealed class HardwareCollector : ICollector
{
    public string SectionName => "hardware";

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

    private static HardwareData Collect()
    {
        string manufacturer = string.Empty;
        string model = string.Empty;
        string serialNumber = string.Empty;

        try
        {
            using var csSearcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in csSearcher.Get())
            {
                manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                model = obj["Model"]?.ToString()?.Trim() ?? string.Empty;
                break;
            }
        }
        catch { /* non-fatal */ }

        var bios = CollectBios(out serialNumber);
        var cpu = CollectCpu();
        var (ramTotalMb, ramModules) = CollectRam();

        return new HardwareData
        {
            Manufacturer = manufacturer,
            Model = model,
            SerialNumber = serialNumber,
            Bios = bios,
            Cpu = cpu,
            RamTotalMb = ramTotalMb,
            RamModules = ramModules
        };
    }

    private static BiosInfo CollectBios(out string serialNumber)
    {
        serialNumber = string.Empty;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
            {
                serialNumber = obj["SerialNumber"]?.ToString()?.Trim() ?? string.Empty;

                string releaseRaw = obj["ReleaseDate"]?.ToString() ?? string.Empty;
                string? releaseIso = null;
                if (!string.IsNullOrEmpty(releaseRaw))
                {
                    try
                    {
                        DateTime dt = ManagementDateTimeConverter.ToDateTime(releaseRaw);
                        releaseIso = dt.ToString("yyyy-MM-dd");
                    }
                    catch { /* leave null */ }
                }

                return new BiosInfo
                {
                    Manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? string.Empty,
                    Version = obj["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? string.Empty,
                    ReleaseDate = releaseIso
                };
            }
        }
        catch { /* non-fatal */ }

        return new BiosInfo();
    }

    private static CpuInfo CollectCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                return new CpuInfo
                {
                    Name = obj["Name"]?.ToString()?.Trim() ?? string.Empty,
                    Cores = Convert.ToInt32(obj["NumberOfCores"] ?? 0),
                    Logical = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0)
                };
            }
        }
        catch { /* non-fatal */ }

        return new CpuInfo();
    }

    private static (long RamTotalMb, int RamModules) CollectRam()
    {
        long totalBytes = 0;
        int modules = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity FROM Win32_PhysicalMemory");
            foreach (ManagementObject obj in searcher.Get())
            {
                modules++;
                totalBytes += Convert.ToInt64(obj["Capacity"] ?? 0L);
            }
        }
        catch { /* non-fatal */ }

        return (totalBytes / (1024 * 1024), modules);
    }
}
