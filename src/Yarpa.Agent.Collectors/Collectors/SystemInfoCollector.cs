using System.Diagnostics;
using System.Management;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects basic machine identity: computer name, current user,
/// domain/workgroup and system uptime.
/// </summary>
public sealed class SystemInfoCollector : ICollector
{
    public string SectionName => "system";

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

    private static SystemInfoData Collect()
    {
        string computerName = Environment.MachineName;
        string userName = Environment.UserName;
        string domainOrWorkgroup = string.Empty;
        long uptimeSeconds = 0;

        try
        {
            using var sysSearcher = new ManagementObjectSearcher(
                "SELECT Domain, PartOfDomain FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in sysSearcher.Get())
            {
                domainOrWorkgroup = obj["Domain"]?.ToString() ?? string.Empty;
                break;
            }
        }
        catch { /* leave empty on permission error */ }

        try
        {
            using var osSearcher = new ManagementObjectSearcher(
                "SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in osSearcher.Get())
            {
                string? lastBootRaw = obj["LastBootUpTime"]?.ToString();
                if (!string.IsNullOrEmpty(lastBootRaw))
                {
                    DateTime bootTime = ManagementDateTimeConverter.ToDateTime(lastBootRaw);
                    uptimeSeconds = (long)(DateTime.Now - bootTime).TotalSeconds;
                }
                break;
            }
        }
        catch { /* leave 0 on error */ }

        return new SystemInfoData
        {
            ComputerName = computerName,
            UserName = userName,
            DomainOrWorkgroup = domainOrWorkgroup,
            UptimeSeconds = uptimeSeconds
        };
    }
}
