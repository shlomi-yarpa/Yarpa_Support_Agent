using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Detects the installed Yarpa (Piryon) application version. Piryon has no registry entry
/// and does not appear in "Add/Remove Programs", so detection is file-system based:
///   1. Locate the "psoftw" folder in the root of a fixed drive (C:\psoftw, D:\psoftw, ...).
///   2. Read "psoftw\piryons.ini" and parse the "pexe=" line
///      (e.g. pexe=\psoftw\piryon2.exe.1.0.898.10235 ⇒ version "1.0.898.10235", build 10235).
///   3. Fallback: FileVersionInfo of "psoftw\piryons.exe" or "psoftw\piryon2.exe".
///
/// Folder name, ini file name and executable candidates are configurable via appsettings.json
/// (YarpaDetection section). When nothing is found, the result is status Ok with
/// detectedBy="notFound" — no values are fabricated.
/// </summary>
public sealed class YarpaVersionCollector : ICollector
{
    private const string ProductName = "Piryon";

    private readonly YarpaDetectionOptions _options;

    /// <summary>
    /// Matches a trailing dotted numeric version of at least three segments,
    /// e.g. "1.0.898.10235" inside "\psoftw\piryon2.exe.1.0.898.10235".
    /// </summary>
    private static readonly Regex VersionTailPattern =
        new(@"(\d+(?:\.\d+){2,})\s*$", RegexOptions.Compiled);

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
        foreach (string psoftwDir in EnumeratePsoftwDirectories(options))
        {
            // 1. piryons.ini → pexe= line
            string iniPath = Path.Combine(psoftwDir, options.IniFileName);
            if (TryDetectFromIni(iniPath, out var iniResult))
                return iniResult!;

            // 2. FileVersionInfo fallback on the executable candidates
            if (TryDetectFromExecutables(psoftwDir, options.ExecutableCandidates, out var fileResult))
                return fileResult!;
        }

        return new YarpaVersionData
        {
            Product = ProductName,
            DetectedBy = "notFound"
        };
    }

    /// <summary>
    /// Enumerates candidate "psoftw" directories: the configured explicit paths first,
    /// then "&lt;drive&gt;\psoftw" for every fixed drive.
    /// </summary>
    private static IEnumerable<string> EnumeratePsoftwDirectories(YarpaDetectionOptions options)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in options.ExplicitPsoftwPaths)
        {
            string expanded = Environment.ExpandEnvironmentVariables(raw);
            if (seen.Add(expanded) && SafeDirectoryExists(expanded))
                yield return expanded;
        }

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            yield break;
        }

        foreach (DriveInfo drive in drives)
        {
            bool isFixed;
            try
            {
                isFixed = drive.DriveType == DriveType.Fixed && drive.IsReady;
            }
            catch
            {
                isFixed = false;
            }

            if (!isFixed) continue;

            string candidate = Path.Combine(drive.RootDirectory.FullName, options.PsoftwFolderName);
            if (seen.Add(candidate) && SafeDirectoryExists(candidate))
                yield return candidate;
        }
    }

    private static bool SafeDirectoryExists(string path)
    {
        try { return Directory.Exists(path); }
        catch { return false; }
    }

    private static bool TryDetectFromIni(string iniPath, [NotNullWhen(true)] out YarpaVersionData? result)
    {
        result = null;
        try
        {
            if (!File.Exists(iniPath)) return false;

            foreach (string line in File.ReadLines(iniPath))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("pexe", StringComparison.OrdinalIgnoreCase))
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;

                string value = trimmed[(eq + 1)..].Trim();
                if (!TryParseVersionFromPexe(value, out string version, out int? build))
                    continue;

                result = new YarpaVersionData
                {
                    Product = ProductName,
                    Version = version,
                    Build = build,
                    DetectedBy = "iniFile",
                    InstallPath = Path.GetDirectoryName(iniPath)
                };
                return true;
            }
        }
        catch { /* fall through to executable fallback */ }

        return false;
    }

    /// <summary>
    /// Parses the version out of a pexe value such as "\psoftw\piryon2.exe.1.0.898.10235".
    /// Prefers the segment following ".exe.", otherwise falls back to the trailing dotted
    /// numeric run. Returns false when no plausible version is present.
    /// </summary>
    internal static bool TryParseVersionFromPexe(string pexeValue, out string version, out int? build)
    {
        version = string.Empty;
        build = null;

        if (string.IsNullOrWhiteSpace(pexeValue))
            return false;

        string candidate;
        int exeIdx = pexeValue.IndexOf(".exe.", StringComparison.OrdinalIgnoreCase);
        if (exeIdx >= 0)
        {
            candidate = pexeValue[(exeIdx + ".exe.".Length)..].Trim();
        }
        else
        {
            Match m = VersionTailPattern.Match(pexeValue);
            if (!m.Success) return false;
            candidate = m.Groups[1].Value;
        }

        candidate = candidate.Trim().Trim('.');

        // Keep only a leading dotted-numeric run (drop any trailing garbage).
        Match vm = Regex.Match(candidate, @"^\d+(?:\.\d+)+");
        if (!vm.Success)
            return false;

        version = vm.Value;
        build = ParseBuild(version);
        return true;
    }

    /// <summary>Returns the last dotted segment of a version as an integer, or null.</summary>
    internal static int? ParseBuild(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        string last = version.Split('.').Last();
        return int.TryParse(last, out int b) ? b : null;
    }

    private static bool TryDetectFromExecutables(
        string psoftwDir,
        IReadOnlyList<string> executableCandidates,
        [NotNullWhen(true)] out YarpaVersionData? result)
    {
        result = null;
        foreach (string exeName in executableCandidates)
        {
            try
            {
                string path = Path.Combine(psoftwDir, exeName);
                if (!File.Exists(path)) continue;

                var fvi = FileVersionInfo.GetVersionInfo(path);

                // 4-part numeric version, matching the Delphi GetVer() convention.
                string version = $"{fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}.{fvi.FilePrivatePart}";
                bool hasVersion = fvi.FileMajorPart > 0 || fvi.FileMinorPart > 0
                    || fvi.FileBuildPart > 0 || fvi.FilePrivatePart > 0;
                if (!hasVersion) continue;

                result = new YarpaVersionData
                {
                    Product = ProductName,
                    Version = version,
                    Build = ParseBuild(version),
                    DetectedBy = "fileVersion",
                    InstallPath = psoftwDir
                };
                return true;
            }
            catch { /* try next candidate */ }
        }

        return false;
    }
}

/// <summary>
/// Configuration for Piryon version detection. Bound from "YarpaDetection" in appsettings.json.
/// </summary>
public sealed class YarpaDetectionOptions
{
    public const string SectionName = "YarpaDetection";

    /// <summary>Name of the Piryon folder searched in the root of each fixed drive. Default "psoftw".</summary>
    public string PsoftwFolderName { get; init; } = "psoftw";

    /// <summary>Name of the Piryon ini file inside the psoftw folder. Default "piryons.ini".</summary>
    public string IniFileName { get; init; } = "piryons.ini";

    /// <summary>Executable file names probed for FileVersionInfo when the ini is missing.</summary>
    public IReadOnlyList<string> ExecutableCandidates { get; init; } =
    [
        "piryons.exe",
        "piryon2.exe"
    ];

    /// <summary>
    /// Optional explicit psoftw directory paths checked before drive scanning
    /// (supports environment variables). Empty by default.
    /// </summary>
    public IReadOnlyList<string> ExplicitPsoftwPaths { get; init; } = [];
}
