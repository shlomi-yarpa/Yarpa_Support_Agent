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

    /// <summary>
    /// Executable file name backing the service (e.g. "Meusensrv.exe"), parsed from the
    /// service binary path. Enables matching monitored services by EXE name in addition to
    /// the service name. Null when the path is unavailable.
    /// </summary>
    [JsonPropertyName("exeName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExeName { get; init; }
}
