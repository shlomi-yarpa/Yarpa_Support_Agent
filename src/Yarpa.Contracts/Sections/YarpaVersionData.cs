using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>Yarpa application version detected on the machine.</summary>
public sealed class YarpaVersionData
{
    [JsonPropertyName("product")]
    public string? Product { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>
    /// Numeric build number — the last dotted segment of <see cref="Version"/>
    /// (e.g. version "1.0.898.10235" ⇒ build 10235). Used by the OldSoftwareVersion alert.
    /// </summary>
    [JsonPropertyName("build")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Build { get; init; }

    /// <summary>
    /// How the version was detected: "iniFile", "fileVersion", or "notFound".
    /// </summary>
    [JsonPropertyName("detectedBy")]
    public string DetectedBy { get; init; } = string.Empty;

    [JsonPropertyName("installPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstallPath { get; init; }
}
