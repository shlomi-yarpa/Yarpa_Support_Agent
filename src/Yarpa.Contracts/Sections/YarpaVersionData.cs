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
    /// How the version was detected: "registry", "fileVersion", "configFile", or "notFound".
    /// </summary>
    [JsonPropertyName("detectedBy")]
    public string DetectedBy { get; init; } = string.Empty;

    [JsonPropertyName("installPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstallPath { get; init; }
}
