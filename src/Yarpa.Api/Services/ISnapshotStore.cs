using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services;

/// <summary>Result produced by <see cref="ISnapshotStore.StoreAsync"/>.</summary>
public sealed class SnapshotStoreResult
{
    public Guid SnapshotId { get; init; }
    public string MachineId { get; init; } = string.Empty;

    /// <summary>Number of detected changes produced by this snapshot.</summary>
    public int Changes { get; init; }

    /// <summary>Number of alerts currently open for the machine after processing this snapshot.</summary>
    public int Alerts { get; init; }

    /// <summary>True when this snapshot was newly stored; false when it already existed (idempotent re-send).</summary>
    public bool IsNew { get; init; }
}

/// <summary>
/// Persists a <see cref="DiagnosticsSnapshot"/> and updates the machine's
/// last-seen metadata. Returns a result indicating whether the snapshot was new.
/// </summary>
public interface ISnapshotStore
{
    Task<SnapshotStoreResult> StoreAsync(
        DiagnosticsSnapshot snapshot,
        string rawJson,
        MachineEntity machine,
        CancellationToken ct);
}
