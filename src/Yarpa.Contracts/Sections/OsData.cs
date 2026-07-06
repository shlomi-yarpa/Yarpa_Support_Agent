using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>
/// Payload for the "os" section: Windows version details.
/// </summary>
public sealed class OsData
{
    [JsonPropertyName("caption")]
    public string Caption { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("build")]
    public string Build { get; init; } = string.Empty;

    [JsonPropertyName("edition")]
    public string Edition { get; init; } = string.Empty;

    /// <summary>Architecture string, e.g. "64-bit".</summary>
    [JsonPropertyName("architecture")]
    public string Architecture { get; init; } = string.Empty;

    /// <summary>Locale/language tag, e.g. "he-IL".</summary>
    [JsonPropertyName("language")]
    public string Language { get; init; } = string.Empty;
}
