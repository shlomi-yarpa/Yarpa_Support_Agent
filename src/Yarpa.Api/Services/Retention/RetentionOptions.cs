namespace Yarpa.Api.Services.Retention;

/// <summary>
/// Configurable snapshot retention policy. Bound from the "Retention" section of
/// appsettings.json. The store stays append-only during normal operation; this policy
/// only prunes old raw snapshots as a controlled, opt-in background job.
///
/// Safety invariants (always enforced regardless of settings):
///  - A machine's most recent snapshot (LastSnapshotId) is never deleted.
///  - Snapshots referenced by a Change or an Alert are never deleted.
///  - Historical Changes and Alerts themselves are never deleted.
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Master switch. Disabled by default — no data is ever pruned unless turned on.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Snapshots older than this many days are candidates for deletion. Default 180.</summary>
    public int RetainDays { get; set; } = 180;

    /// <summary>
    /// Always keep at least this many of the most recent snapshots per machine,
    /// even if they are older than <see cref="RetainDays"/>. Default 10.
    /// </summary>
    public int MinSnapshotsPerMachine { get; set; } = 10;

    /// <summary>Maximum snapshots deleted in a single run (batch cap for control). Default 500.</summary>
    public int MaxDeletePerRun { get; set; } = 500;

    /// <summary>How often (hours) the background retention job runs. Default 24.</summary>
    public double ScanIntervalHours { get; set; } = 24;
}
