using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>
/// A payment terminal device identified by cross-referencing USB VID/PID
/// against the vendor mapping table.
/// </summary>
public sealed class PaymentTerminalInfo
{
    [JsonPropertyName("vendor")]
    public string Vendor { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("comPort")]
    public string? ComPort { get; init; }

    [JsonPropertyName("vid")]
    public string Vid { get; init; } = string.Empty;

    [JsonPropertyName("pid")]
    public string Pid { get; init; } = string.Empty;
}
