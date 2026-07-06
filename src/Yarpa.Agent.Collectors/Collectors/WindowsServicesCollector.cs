using System.Diagnostics;
using System.Management;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects the status of monitored Windows services: Yarpa, SQL Server, IIS, and any
/// additional names supplied via the watchlist. The watchlist supports wildcard patterns
/// using '*' (e.g. "MSSQL*" matches all SQL instances).
/// Source: WMI Win32_Service.
/// </summary>
public sealed class WindowsServicesCollector : ICollector
{
    private readonly IReadOnlyList<string> _watchlist;

    /// <summary>
    /// Default patterns that are always monitored regardless of configuration.
    /// Wildcards ('*') are matched as prefix/suffix/substring matches.
    /// </summary>
    private static readonly string[] DefaultPatterns =
    [
        "MSSQL*",       // all SQL Server instances
        "SQLAgent*",    // SQL Agent
        "SQLTELEMETRY", // SQL telemetry
        "W3SVC",        // IIS World Wide Web Publishing
        "WAS",          // IIS Windows Activation Service
        "Yarpa*"        // any Yarpa service
    ];

    public WindowsServicesCollector(IReadOnlyList<string>? watchlist = null)
    {
        _watchlist = watchlist != null && watchlist.Count > 0
            ? watchlist
            : DefaultPatterns;
    }

    public string SectionName => "services";

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var data = await Task.Run(() => Collect(_watchlist), ct);
            sw.Stop();
            return CollectorResult.Ok(SectionName, data, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CollectorResult.Failed(SectionName, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static List<ServiceInfo> Collect(IReadOnlyList<string> watchlist)
    {
        // Fetch all services via WMI; we filter in-process (WMI WHERE on State/StartMode is slow)
        var services = new List<ServiceInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, State, StartMode FROM Win32_Service");

        foreach (ManagementObject obj in searcher.Get())
        {
            string name = obj["Name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (!MatchesWatchlist(name, watchlist)) continue;

            services.Add(new ServiceInfo
            {
                Name = name,
                DisplayName = obj["DisplayName"]?.ToString() ?? string.Empty,
                State = obj["State"]?.ToString() ?? string.Empty,
                StartMode = obj["StartMode"]?.ToString() ?? string.Empty
            });
        }

        return services.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Checks whether <paramref name="serviceName"/> matches any pattern in the watchlist.
    /// A pattern ending with '*' is treated as a prefix match; '*' anywhere is treated as
    /// a wildcard (start/end/middle). Comparison is case-insensitive.
    /// </summary>
    public static bool MatchesWatchlist(string serviceName, IReadOnlyList<string> watchlist)
    {
        foreach (string pattern in watchlist)
        {
            if (MatchesPattern(serviceName, pattern))
                return true;
        }
        return false;
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        if (!pattern.Contains('*'))
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);

        // Simple wildcard: split on '*' and check each segment appears in order
        string[] parts = pattern.Split('*');
        int pos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;

            int idx = name.IndexOf(parts[i], pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            // For the first non-empty part, if the pattern doesn't start with '*', enforce start
            if (i == 0 && !pattern.StartsWith('*') && idx != 0) return false;

            pos = idx + parts[i].Length;
        }

        // If pattern doesn't end with '*', the last segment must reach the end
        if (!pattern.EndsWith('*') && pos != name.Length) return false;

        return true;
    }
}
