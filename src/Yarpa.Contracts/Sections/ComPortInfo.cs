using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>A COM (serial) port present on the machine.</summary>
public sealed class ComPortInfo
{
    [JsonPropertyName("port")]
    public string Port { get; init; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; init; }
}
