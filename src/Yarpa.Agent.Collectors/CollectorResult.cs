using Yarpa.Contracts;

namespace Yarpa.Agent.Collectors;

/// <summary>
/// Result of a single <see cref="ICollector"/> run. Failures are represented as data
/// (<see cref="Status"/> = <see cref="CollectorStatus.Error"/>) rather than exceptions,
/// so a single collector cannot break the overall collection.
/// </summary>
public sealed class CollectorResult
{
    public required string SectionName { get; init; }

    public required CollectorStatus Status { get; init; }

    public object? Data { get; init; }

    public string? Error { get; init; }

    /// <summary>Wall-clock duration of the collection in milliseconds.</summary>
    public long DurationMs { get; init; }

    public static CollectorResult Ok(string sectionName, object data, long durationMs) =>
        new()
        {
            SectionName = sectionName,
            Status = CollectorStatus.Ok,
            Data = data,
            DurationMs = durationMs
        };

    public static CollectorResult Partial(string sectionName, object data, string error, long durationMs) =>
        new()
        {
            SectionName = sectionName,
            Status = CollectorStatus.Partial,
            Data = data,
            Error = error,
            DurationMs = durationMs
        };

    public static CollectorResult Failed(string sectionName, string error, long durationMs) =>
        new()
        {
            SectionName = sectionName,
            Status = CollectorStatus.Error,
            Data = null,
            Error = error,
            DurationMs = durationMs
        };

    /// <summary>Projects this result onto the wire-format section envelope.</summary>
    public SnapshotSection ToSection() =>
        new()
        {
            Status = Status,
            Data = Data,
            Error = Error
        };
}
