using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Win32;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Detects the installed Yarpa application version using a configurable detection strategy:
///   1. Registry: checks a list of registry key paths for a "DisplayVersion" or "Version" value.
///   2. FileVersionInfo: checks a list of executable/DLL paths for file version information.
///   3. ConfigFile: checks a list of text/JSON files that may contain a version string.
///
/// Detection paths are configurable without code changes via appsettings.json
/// (YarpaDetection section). When exact Yarpa registry/file paths are unknown,
/// the result is returned as detectedBy="notFound" – no fake values are fabricated.
/// </summary>
public sealed class YarpaVersionCollector : ICollector
{
    private readonly YarpaDetectionOptions _options;

    public YarpaVersionCollector(YarpaDetectionOptions? options = null)
    {
        _options = options ?? new YarpaDetectionOptions();
    }

    public string SectionName => "yarpaVersion";

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var data = await Task.Run(() => Detect(_options), ct);
            sw.Stop();
            return CollectorResult.Ok(SectionName, data, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CollectorResult.Failed(SectionName, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static YarpaVersionData Detect(YarpaDetectionOptions options)
    {
        // 1. Registry
        if (TryDetectFromRegistry(options.RegistryKeys, out var regResult))
            return regResult!;

        // 2. FileVersionInfo on executable/DLL
        if (TryDetectFromFile(options.ExecutablePaths, out var fileResult))
            return fileResult!;

        // 3. Config / version text file
        if (TryDetectFromConfigFile(options.ConfigFilePaths, out var configResult))
            return configResult!;

        return new YarpaVersionData
        {
            DetectedBy = "notFound"
        };
    }

    private static bool TryDetectFromRegistry(
        IReadOnlyList<string> keyPaths,
        [NotNullWhen(true)] out YarpaVersionData? result)
    {
        result = null;
        foreach (string keyPath in keyPaths)
        {
            try
            {
                (RegistryHive hive, string subKey) = SplitHiveAndSubKey(keyPath);
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(subKey);
                if (key == null) continue;

                string? version = key.GetValue("DisplayVersion")?.ToString()
                    ?? key.GetValue("Version")?.ToString();
                if (string.IsNullOrEmpty(version)) continue;

                string? product = key.GetValue("DisplayName")?.ToString()
                    ?? key.GetValue("ProductName")?.ToString();
                string? installPath = key.GetValue("InstallLocation")?.ToString()
                    ?? key.GetValue("InstallDir")?.ToString();

                result = new YarpaVersionData
                {
                    Product = string.IsNullOrEmpty(product) ? "Yarpa" : product,
                    Version = version,
                    DetectedBy = "registry",
                    InstallPath = string.IsNullOrEmpty(installPath) ? null : installPath
                };
                return true;
            }
            catch { /* try next path */ }
        }
        return false;
    }

    private static bool TryDetectFromFile(
        IReadOnlyList<string> filePaths,
        [NotNullWhen(true)] out YarpaVersionData? result)
    {
        result = null;
        foreach (string rawPath in filePaths)
        {
            try
            {
                string path = Environment.ExpandEnvironmentVariables(rawPath);
                if (!File.Exists(path)) continue;

                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);

                // Build 4-part numeric version from the raw file version resource,
                // matching the behavior of the Delphi GetVer() function:
                //   dwFileVersionMS shr 16  → FileMajorPart
                //   dwFileVersionMS and $FFFF → FileMinorPart
                //   dwFileVersionLS shr 16  → FileBuildPart
                //   dwFileVersionLS and $FFFF → FilePrivatePart
                string numericVersion = $"{fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}.{fvi.FilePrivatePart}";
                bool hasVersion = fvi.FileMajorPart > 0 || fvi.FileMinorPart > 0 || fvi.FileBuildPart > 0;
                if (!hasVersion) continue;

                result = new YarpaVersionData
                {
                    Product = string.IsNullOrEmpty(fvi.ProductName) ? "Yarpa" : fvi.ProductName,
                    Version = numericVersion,
                    DetectedBy = "fileVersion",
                    InstallPath = Path.GetDirectoryName(path)
                };
                return true;
            }
            catch { /* try next path */ }
        }
        return false;
    }

    private static bool TryDetectFromConfigFile(
        IReadOnlyList<string> configPaths,
        [NotNullWhen(true)] out YarpaVersionData? result)
    {
        result = null;
        foreach (string rawPath in configPaths)
        {
            try
            {
                string path = Environment.ExpandEnvironmentVariables(rawPath);
                if (!File.Exists(path)) continue;

                string content = File.ReadAllText(path).Trim();
                if (string.IsNullOrEmpty(content)) continue;

                // Try JSON first: {"version":"x.y.z"} or {"Version":"x.y.z"}
                if (content.StartsWith('{'))
                {
                    using var doc = JsonDocument.Parse(content);
                    string? v = null;
                    if (doc.RootElement.TryGetProperty("version", out var vEl) ||
                        doc.RootElement.TryGetProperty("Version", out vEl))
                    {
                        v = vEl.GetString();
                    }

                    if (!string.IsNullOrEmpty(v))
                    {
                        result = new YarpaVersionData
                        {
                            Version = v,
                            DetectedBy = "configFile",
                            InstallPath = Path.GetDirectoryName(path)
                        };
                        return true;
                    }
                }

                // Plain text: first line is the version string
                string firstLine = content.Split('\n')[0].Trim();
                if (!string.IsNullOrEmpty(firstLine))
                {
                    result = new YarpaVersionData
                    {
                        Version = firstLine,
                        DetectedBy = "configFile",
                        InstallPath = Path.GetDirectoryName(path)
                    };
                    return true;
                }
            }
            catch { /* try next path */ }
        }
        return false;
    }

    private static (RegistryHive Hive, string SubKey) SplitHiveAndSubKey(string fullPath)
    {
        int sep = fullPath.IndexOf('\\');
        if (sep < 0) return (RegistryHive.LocalMachine, fullPath);

        string hiveName = fullPath[..sep].ToUpperInvariant();
        string subKey = fullPath[(sep + 1)..];

        RegistryHive hive = hiveName switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            _ => RegistryHive.LocalMachine
        };

        return (hive, subKey);
    }
}

/// <summary>
/// Configuration for Yarpa version detection paths. Bound from "YarpaDetection" in appsettings.json.
/// All paths support environment variable expansion (e.g. %ProgramFiles%).
/// </summary>
public sealed class YarpaDetectionOptions
{
    public const string SectionName = "YarpaDetection";

    /// <summary>Registry key full paths to check for version info.</summary>
    public IReadOnlyList<string> RegistryKeys { get; init; } =
    [
        @"HKLM\SOFTWARE\Yarpa\Yarpa ERP",
        @"HKLM\SOFTWARE\WOW6432Node\Yarpa\Yarpa ERP",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Yarpa ERP",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Yarpa ERP"
    ];

    /// <summary>Executable or DLL paths to check for FileVersionInfo.</summary>
    public IReadOnlyList<string> ExecutablePaths { get; init; } =
    [
        // Primary Yarpa ERP executable (piryon2.exe) — common deployment paths
        @"D:\psoftw\piryon2.exe",
        @"C:\psoftw\piryon2.exe",
        @"D:\psoft\piryon2.exe",
        @"C:\psoft\piryon2.exe",
        // Variant names used in some installations
        @"D:\psoftw\piryon3.exe",
        @"D:\psoftw\piryon5.exe",
        @"D:\psoftw\piryonS.exe",
        @"C:\psoftw\piryon3.exe",
        @"C:\psoftw\piryon5.exe",
        // Legacy / generic fallback paths
        @"%ProgramFiles%\Yarpa\YarpaERP.exe",
        @"%ProgramFiles(x86)%\Yarpa\YarpaERP.exe",
        @"%ProgramFiles%\Yarpa\Yarpa.exe",
        @"%ProgramFiles(x86)%\Yarpa\Yarpa.exe"
    ];

    /// <summary>Config/version text files to check for a version string.</summary>
    public IReadOnlyList<string> ConfigFilePaths { get; init; } =
    [
        @"%ProgramFiles%\Yarpa\version.txt",
        @"%ProgramFiles(x86)%\Yarpa\version.txt",
        @"%ProgramFiles%\Yarpa\version.json",
        @"%ProgramFiles(x86)%\Yarpa\version.json"
    ];
}
