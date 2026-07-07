using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts;

/// <summary>Outcome of an <see cref="IAlertEngine.EvaluateAsync"/> run.</summary>
public sealed class AlertEngineResult
{
    /// <summary>Number of alerts that remain open for the machine after reconciliation.</summary>
    public int OpenAlertCount { get; init; }

    /// <summary>Number of alerts newly raised during this evaluation.</summary>
    public int RaisedCount { get; init; }

    /// <summary>Number of previously open alerts resolved during this evaluation.</summary>
    public int ResolvedCount { get; init; }
}

/// <summary>
/// Runs the registered alert rules against a received snapshot and its detected changes,
/// creating new alerts and resolving cleared ones. The engine stages its changes on the
/// injected DbContext but does not call SaveChanges — the caller (SnapshotStore) persists
/// everything in a single transaction.
/// </summary>
public interface IAlertEngine
{
    Task<AlertEngineResult> EvaluateAsync(
        DiagnosticsSnapshot snapshot,
        MachineEntity machine,
        IReadOnlyList<ChangeEntity> changes,
        CancellationToken ct);
}
