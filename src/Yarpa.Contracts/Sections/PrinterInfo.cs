using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>A single printer installed on the machine.</summary>
public sealed class PrinterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("portName")]
    public string? PortName { get; init; }

    [JsonPropertyName("driver")]
    public string? Driver { get; init; }
}
