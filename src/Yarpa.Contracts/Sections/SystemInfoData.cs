using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>
/// Payload for the "system" section: basic machine identity gathered from the OS.
/// </summary>
public sealed class SystemInfoData
{
    [JsonPropertyName("computerName")]
    public string ComputerName { get; init; } = string.Empty;

    [JsonPropertyName("userName")]
    public string UserName { get; init; } = string.Empty;

    [JsonPropertyName("domainOrWorkgroup")]
    public string DomainOrWorkgroup { get; init; } = string.Empty;

    /// <summary>Seconds since last boot (uptime).</summary>
    [JsonPropertyName("uptimeSeconds")]
    public long UptimeSeconds { get; init; }
}
