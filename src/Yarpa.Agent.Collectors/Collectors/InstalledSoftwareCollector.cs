using System.Diagnostics;
using Microsoft.Win32;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects installed software relevant to support: Yarpa products, .NET Runtime,
/// SQL Server components, and any additional names supplied via the filter list.
/// Source: Registry Uninstall keys (HKLM + HKCU, 32-bit and 64-bit hives).
/// </summary>
public sealed class InstalledSoftwareCollector : ICollector
{
    private readonly IReadOnlyList<string> _nameFilters;

    /// <summary>Default substrings to include. Case-insensitive matching.</summary>
    private static readonly string[] DefaultFilters =
    [
        "yarpa",
        "microsoft .net",
        ".net desktop runtime",
        ".net runtime",
        "sql server",
        "sql native client",
        "sqlncli",
        "microsoft odbc driver"
    ];

    public InstalledSoftwareCollector(IReadOnlyList<string>? nameFilters = null)
    {
        _nameFilters = nameFilters != null && nameFilters.Count > 0
            ? nameFilters
            : DefaultFilters;
    }

    public string SectionName => "installedSoftware";

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (data, partialError) = await Task.Run(() => Collect(_nameFilters), ct);
            sw.Stop();

            if (partialError != null)
                return CollectorResult.Partial(SectionName, data, partialError, sw.ElapsedMilliseconds);

            return CollectorResult.Ok(SectionName, data, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CollectorResult.Failed(SectionName, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static (List<InstalledSoftwareInfo> Software, string? PartialError) Collect(
        IReadOnlyList<string> filters)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var software = new List<InstalledSoftwareInfo>();
        string? partialError = null;

        // All four registry locations for installed programs
        var locations = new[]
        {
            (Hive: Registry.LocalMachine,
             Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Hive: Registry.LocalMachine,
             Path: @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Hive: Registry.CurrentUser,
             Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Hive: Registry.CurrentUser,
             Path: @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var (hive, path) in locations)
        {
            try
            {
                using var root = hive.OpenSubKey(path);
                if (root == null) continue;

                foreach (string subKeyName in root.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = root.OpenSubKey(subKeyName);
                        if (sub == null) continue;

                        string? displayName = sub.GetValue("DisplayName")?.ToString();
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        if (!MatchesFilter(displayName, filters)) continue;

                        // Deduplicate by name+version combination
                        string? version = sub.GetValue("DisplayVersion")?.ToString();
                        string dedupeKey = $"{displayName}|{version}";
                        if (!seen.Add(dedupeKey)) continue;

                        string? rawDate = sub.GetValue("InstallDate")?.ToString();
                        string? installDate = ParseInstallDate(rawDate);

                        software.Add(new InstalledSoftwareInfo
                        {
                            Name = displayName,
                            Version = version,
                            Publisher = sub.GetValue("Publisher")?.ToString(),
                            InstallDate = installDate
                        });
                    }
                    catch { /* skip individual entry; continue with others */ }
                }
            }
            catch (Exception ex) when (hive == Registry.CurrentUser)
            {
                // HKCU can fail without admin on some configurations – mark partial
                partialError = $"HKCU Uninstall query failed: {ex.Message}";
            }
            catch { /* non-fatal for other hives */ }
        }

        return (software.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(), partialError);
    }

    private static bool MatchesFilter(string name, IReadOnlyList<string> filters)
    {
        foreach (string filter in filters)
        {
            if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Parses registry install date "yyyyMMdd" to ISO "yyyy-MM-dd".</summary>
    private static string? ParseInstallDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length != 8) return raw;
        if (DateOnly.TryParseExact(raw, "yyyyMMdd", out DateOnly d))
            return d.ToString("yyyy-MM-dd");
        return raw;
    }
}
