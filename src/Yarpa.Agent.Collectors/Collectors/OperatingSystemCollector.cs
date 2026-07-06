using System.Diagnostics;
using System.Globalization;
using System.Management;
using Microsoft.Win32;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects Windows version, build number, edition, architecture and locale.
/// Primary source: WMI Win32_OperatingSystem; supplemented by the CurrentVersion registry key.
/// </summary>
public sealed class OperatingSystemCollector : ICollector
{
    public string SectionName => "os";

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

    private static OsData Collect()
    {
        string caption = string.Empty;
        string version = string.Empty;
        string build = string.Empty;
        string architecture = string.Empty;
        string language = CultureInfo.CurrentUICulture.Name;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, Version, BuildNumber, OSArchitecture, MUILanguages " +
                "FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                caption = obj["Caption"]?.ToString()?.Trim() ?? string.Empty;
                version = obj["Version"]?.ToString() ?? string.Empty;
                build = obj["BuildNumber"]?.ToString() ?? string.Empty;
                architecture = obj["OSArchitecture"]?.ToString() ?? string.Empty;

                // MUILanguages is a string array; take the first entry when available
                if (obj["MUILanguages"] is string[] langs && langs.Length > 0)
                    language = langs[0];

                break;
            }
        }
        catch { /* fall through */ }

        // Supplement / override edition from the registry CurrentVersion key
        string edition = string.Empty;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                edition = key.GetValue("EditionID")?.ToString() ?? string.Empty;

                // Use registry build when WMI didn't return one (some server SKUs)
                if (string.IsNullOrEmpty(build))
                    build = key.GetValue("CurrentBuildNumber")?.ToString() ?? string.Empty;
            }
        }
        catch { /* non-fatal */ }

        return new OsData
        {
            Caption = caption,
            Version = version,
            Build = build,
            Edition = edition,
            Architecture = architecture,
            Language = language
        };
    }
}
