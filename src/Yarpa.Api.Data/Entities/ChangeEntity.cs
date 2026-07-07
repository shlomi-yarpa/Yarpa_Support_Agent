namespace Yarpa.Api.Data.Entities;

/// <summary>
/// A single detected difference between two consecutive snapshots for a machine.
/// Records are append-only; never updated or deleted.
/// </summary>
public sealed class ChangeEntity
{
    /// <summary>Auto-increment PK.</summary>
    public long ChangeId { get; set; }

    /// <summary>The machine this change belongs to.</summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>The snapshot that caused this change to be detected (the newer snapshot).</summary>
    public Guid SnapshotId { get; set; }

    /// <summary>
    /// Discriminator string matching one of the <see cref="ChangeType"/> constants,
    /// e.g. "OsChanged", "DeviceAdded", "ServiceStateChanged".
    /// </summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>Name of the section the change was detected in, e.g. "os", "usbDevices".</summary>
    public string SectionName { get; set; } = string.Empty;

    /// <summary>Previous value (JSON string or plain string). Null for additions.</summary>
    public string? OldValue { get; set; }

    /// <summary>New value (JSON string or plain string). Null for removals.</summary>
    public string? NewValue { get; set; }

    /// <summary>UTC timestamp when this change was detected by the server.</summary>
    public DateTime DetectedAtUtc { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public MachineEntity Machine { get; set; } = null!;
    public SnapshotEntity Snapshot { get; set; } = null!;
}
