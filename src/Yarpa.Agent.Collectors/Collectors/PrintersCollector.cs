using System.Diagnostics;
using System.Management;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects all installed printers: name, default flag, status, port and driver.
/// Source: WMI Win32_Printer.
/// </summary>
public sealed class PrintersCollector : ICollector
{
    public string SectionName => "printers";

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

    private static List<PrinterInfo> Collect()
    {
        var printers = new List<PrinterInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, Default, PrinterStatus, PortName, DriverName FROM Win32_Printer");

        foreach (ManagementObject obj in searcher.Get())
        {
            string name = obj["Name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;

            bool isDefault = Convert.ToBoolean(obj["Default"] ?? false);
            uint statusCode = Convert.ToUInt32(obj["PrinterStatus"] ?? 0u);

            printers.Add(new PrinterInfo
            {
                Name = name,
                IsDefault = isDefault,
                Status = MapStatus(statusCode),
                PortName = obj["PortName"]?.ToString(),
                Driver = obj["DriverName"]?.ToString()
            });
        }

        return printers;
    }

    /// <summary>Maps Win32_Printer PrinterStatus numeric value to a readable string.</summary>
    private static string MapStatus(uint code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Idle",
        4 => "Printing",
        5 => "WarmingUp",
        6 => "StoppedPrinting",
        7 => "Offline",
        _ => "Unknown"
    };
}
