using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>A Windows service entry from the monitored watchlist.</summary>
public sealed class ServiceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("startMode")]
    public string StartMode { get; init; } = string.Empty;
}
