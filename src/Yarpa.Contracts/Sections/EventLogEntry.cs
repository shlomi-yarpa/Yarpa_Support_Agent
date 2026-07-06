using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>A single Windows Event Log entry (Error/Warning/Critical) within the collection window.</summary>
public sealed class EventLogEntry
{
    [JsonPropertyName("log")]
    public string Log { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("eventId")]
    public long EventId { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; } = string.Empty;

    [JsonPropertyName("timeUtc")]
    public DateTimeOffset TimeUtc { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
