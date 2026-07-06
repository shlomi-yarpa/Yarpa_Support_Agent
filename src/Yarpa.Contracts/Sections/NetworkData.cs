using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>
/// Network section payload: active adapters plus optional external IP.
/// externalIp is nested inside data (consistent with the generic SnapshotSection envelope).
/// </summary>
public sealed class NetworkData
{
    [JsonPropertyName("adapters")]
    public List<NetworkAdapterInfo> Adapters { get; init; } = new();

    [JsonPropertyName("externalIp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalIp { get; init; }
}
