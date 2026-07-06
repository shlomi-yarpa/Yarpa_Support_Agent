using System.Text.Json.Serialization;

namespace Yarpa.Contracts;

/// <summary>
/// Generic envelope for a single section of a <see cref="DiagnosticsSnapshot"/>.
/// Every section carries its own <see cref="Status"/> so that a partial collection
/// is still a valid snapshot. A failed section sets <see cref="Status"/> to
/// <see cref="CollectorStatus.Error"/>, leaves <see cref="Data"/> null and populates
/// <see cref="Error"/>.
/// </summary>
public sealed class SnapshotSection
{
    [JsonPropertyName("status")]
    public CollectorStatus Status { get; init; }

    /// <summary>
    /// Section payload. Shape depends on the section (object or array) and is defined
    /// per-section in later stages. Null when <see cref="Status"/> is
    /// <see cref="CollectorStatus.Error"/>.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }

    /// <summary>Error message when the section failed; otherwise null.</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    public static SnapshotSection FromData(object data) =>
        new() { Status = CollectorStatus.Ok, Data = data };

    public static SnapshotSection FromError(string error) =>
        new() { Status = CollectorStatus.Error, Data = null, Error = error };
}
