using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Detects SQL Server installations: whether SQL is installed, which instances exist,
/// their version strings, and the current service state of each instance.
/// Source: Registry HKLM\SOFTWARE\Microsoft\Microsoft SQL Server + WMI Win32_Service.
/// </summary>
public sealed class SqlServerCollector : ICollector
{
    public string SectionName => "sqlServer";

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

    private static SqlServerData Collect()
    {
        var instances = FindInstances();
        EnrichWithServiceState(instances);

        return new SqlServerData
        {
            Installed = instances.Count > 0,
            Instances = instances
        };
    }

    /// <summary>
    /// Reads SQL Server instance names from the registry.
    /// HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\InstalledInstances lists all instances.
    /// </summary>
    private static List<SqlInstanceInfo> FindInstances()
    {
        var instances = new List<SqlInstanceInfo>();

        // Try 64-bit hive first, then 32-bit
        string[] registryPaths =
        [
            @"SOFTWARE\Microsoft\Microsoft SQL Server",
            @"SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server"
        ];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string regPath in registryPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key == null) continue;

                object? installed = key.GetValue("InstalledInstances");
                if (installed is not string[] instanceNames) continue;

                foreach (string instanceName in instanceNames)
                {
                    if (!seen.Add(instanceName)) continue;

                    string? version = ReadInstanceVersion(key, instanceName);
                    instances.Add(new SqlInstanceInfo
                    {
                        Name = instanceName,
                        Version = version,
                        ServiceState = string.Empty   // filled in by EnrichWithServiceState
                    });
                }
            }
            catch { /* non-fatal */ }
        }

        return instances;
    }

    private static string? ReadInstanceVersion(RegistryKey sqlRootKey, string instanceName)
    {
        try
        {
            // Version is under HKLM\...\<instanceName>\MSSQLServer\CurrentVersion\CurrentVersion
            // or HKLM\...\Instance Names\SQL\<instanceName> → registry key name like MSSQL15.MSSQLSERVER
            object? keyNameObj = sqlRootKey
                .OpenSubKey(@"Instance Names\SQL")?
                .GetValue(instanceName);

            string? keyName = keyNameObj?.ToString();
            if (string.IsNullOrEmpty(keyName)) return null;

            using var versionKey = sqlRootKey.OpenSubKey($@"{keyName}\MSSQLServer\CurrentVersion");
            return versionKey?.GetValue("CurrentVersion")?.ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// Looks up each instance in WMI Win32_Service and fills in the ServiceState.
    /// SQL Server services are named "MSSQLSERVER" (default) or "MSSQL$instanceName".
    /// </summary>
    private static void EnrichWithServiceState(List<SqlInstanceInfo> instances)
    {
        if (instances.Count == 0) return;

        // Build expected service names
        var serviceNameMap = new Dictionary<string, SqlInstanceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var inst in instances)
        {
            string svcName = inst.Name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                ? "MSSQLSERVER"
                : $"MSSQL${inst.Name}";

            serviceNameMap[svcName] = inst;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, State FROM Win32_Service WHERE Name LIKE 'MSSQL%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                string svcName = obj["Name"]?.ToString() ?? string.Empty;
                string state = obj["State"]?.ToString() ?? string.Empty;

                if (serviceNameMap.TryGetValue(svcName, out var inst))
                {
                    // Replace the instance with an updated one (records are sealed)
                    int idx = instances.IndexOf(inst);
                    if (idx >= 0)
                    {
                        instances[idx] = new SqlInstanceInfo
                        {
                            Name = inst.Name,
                            Version = inst.Version,
                            ServiceState = state
                        };
                    }
                }
            }
        }
        catch { /* non-fatal; ServiceState remains empty */ }
    }
}
