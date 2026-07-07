namespace Yarpa.Api.Data.Entities;

/// <summary>
/// An automatically generated support alert for a machine, produced by the server-side
/// alert engine from a snapshot and its detected changes (or by the periodic
/// no-recent-contact checker). At most one alert of a given <see cref="AlertType"/>
/// is left open per machine; when the underlying condition clears the alert is
/// resolved rather than deleted (append-only history).
/// </summary>
public sealed class AlertEntity
{
    /// <summary>Auto-increment PK.</summary>
    public long AlertId { get; set; }

    /// <summary>The machine this alert belongs to.</summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// Discriminator string matching one of the <see cref="AlertType"/> constants,
    /// e.g. "ServiceDown", "DiskAlmostFull", "SqlNotRunning".
    /// </summary>
    public string AlertType { get; set; } = string.Empty;

    /// <summary>Severity level: one of the <see cref="AlertSeverity"/> constants (info / warning / critical).</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Human-readable message describing the alert (Hebrew).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Lifecycle state: one of the <see cref="AlertState"/> constants (open / resolved).</summary>
    public string State { get; set; } = AlertState.Open;

    /// <summary>UTC timestamp when the alert was first raised.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp when the alert was resolved (null while open).</summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>
    /// The snapshot that raised this alert. Null for alerts that are not derived from a
    /// snapshot (e.g. the time-based NoRecentContact alert).
    /// </summary>
    public Guid? SourceSnapshotId { get; set; }

    /// <summary>The change that triggered this alert, when it is derived from a specific change. Nullable.</summary>
    public long? SourceChangeId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public MachineEntity Machine { get; set; } = null!;
    public SnapshotEntity? SourceSnapshot { get; set; }
    public ChangeEntity? SourceChange { get; set; }
}
