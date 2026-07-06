using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>A single active network adapter and its configuration.</summary>
public sealed class NetworkAdapterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("mac")]
    public string Mac { get; init; } = string.Empty;

    [JsonPropertyName("ipv4")]
    public string? Ipv4 { get; init; }

    [JsonPropertyName("gateway")]
    public string? Gateway { get; init; }

    [JsonPropertyName("dns")]
    public List<string> Dns { get; init; } = new();
}
