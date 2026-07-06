using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>A relevant installed software entry from the Windows registry.</summary>
public sealed class InstalledSoftwareInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; init; }

    [JsonPropertyName("installDate")]
    public string? InstallDate { get; init; }
}
