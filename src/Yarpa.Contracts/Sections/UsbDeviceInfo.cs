using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>A USB device currently connected to the machine.</summary>
public sealed class UsbDeviceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("vid")]
    public string? Vid { get; init; }

    [JsonPropertyName("pid")]
    public string? Pid { get; init; }

    [JsonPropertyName("deviceClass")]
    public string? DeviceClass { get; init; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; init; }
}
