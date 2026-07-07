using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts;

/// <summary>
/// Immutable input passed to every <see cref="IAlertRule"/> when a snapshot is processed.
/// Carries the newly received snapshot, the machine, the changes detected by the comparer
/// for this snapshot, and the configured thresholds.
/// </summary>
public sealed class AlertEvaluationContext
{
    public AlertEvaluationContext(
        DiagnosticsSnapshot snapshot,
        MachineEntity machine,
        IReadOnlyList<ChangeEntity> changes,
        AlertOptions options,
        DateTime nowUtc)
    {
        Snapshot = snapshot;
        Machine = machine;
        Changes = changes;
        Options = options;
        NowUtc = nowUtc;
    }

    public DiagnosticsSnapshot Snapshot { get; }
    public MachineEntity Machine { get; }
    public IReadOnlyList<ChangeEntity> Changes { get; }
    public AlertOptions Options { get; }
    public DateTime NowUtc { get; }
}
