using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>A single SQL Server instance detected on the machine.</summary>
public sealed class SqlInstanceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("serviceState")]
    public string ServiceState { get; init; } = string.Empty;
}
