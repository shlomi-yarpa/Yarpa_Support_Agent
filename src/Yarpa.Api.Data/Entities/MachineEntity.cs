namespace Yarpa.Api.Data.Entities;

/// <summary>
/// Represents a client machine identified by the stable fingerprint produced by the Agent.
/// Machines are auto-registered on first snapshot receipt.
/// </summary>
public sealed class MachineEntity
{
    /// <summary>Primary key — the stable fingerprint string computed by the Agent.</summary>
    public string MachineId { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    /// <summary>Computer name as reported in the most recent snapshot.</summary>
    public string ComputerName { get; set; } = string.Empty;

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    /// <summary>SnapshotId of the most recently accepted snapshot (null until first snapshot).</summary>
    public Guid? LastSnapshotId { get; set; }

    // Navigation
    public CustomerEntity Customer { get; set; } = null!;
    public ICollection<SnapshotEntity> Snapshots { get; set; } = new List<SnapshotEntity>();
}
