namespace Yarpa.Api.Services;

/// <summary>
/// Process-wide, thread-safe counters for basic operational observability of snapshot
/// ingestion. Registered as a singleton and surfaced through the /metrics endpoint.
/// Counters are cumulative since process start and reset only on restart.
/// </summary>
public sealed class SnapshotMetrics
{
    private long _received;
    private long _accepted;
    private long _duplicate;
    private long _rejected;
    private long _failed;

    /// <summary>UTC time the process (and therefore the counters) started.</summary>
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>Total snapshot POST requests that reached the controller.</summary>
    public long Received => Interlocked.Read(ref _received);

    /// <summary>Snapshots stored as new (HTTP 202).</summary>
    public long Accepted => Interlocked.Read(ref _accepted);

    /// <summary>Snapshots ignored as idempotent duplicates (HTTP 200).</summary>
    public long Duplicate => Interlocked.Read(ref _duplicate);

    /// <summary>Snapshots rejected for validation / bad-request reasons (HTTP 400/413).</summary>
    public long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>Snapshots that failed with an unexpected server error (HTTP 5xx).</summary>
    public long Failed => Interlocked.Read(ref _failed);

    public void IncrementReceived() => Interlocked.Increment(ref _received);
    public void IncrementAccepted() => Interlocked.Increment(ref _accepted);
    public void IncrementDuplicate() => Interlocked.Increment(ref _duplicate);
    public void IncrementRejected() => Interlocked.Increment(ref _rejected);
    public void IncrementFailed() => Interlocked.Increment(ref _failed);
}
