using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts;

/// <summary>
/// Modular alert engine. Evaluates every registered <see cref="IAlertRule"/> against the
/// received snapshot and reconciles the result with the machine's currently open alerts:
/// <list type="bullet">
///   <item>Raise + no open alert of that type → create a new open alert.</item>
///   <item>Raise + an open alert already exists → leave it open (no duplicate).</item>
///   <item>Clear + an open alert exists → resolve it (State = resolved, ResolvedAtUtc set).</item>
///   <item>Leave → do nothing.</item>
/// </list>
/// Because receiving a snapshot is itself a sign of contact, any open NoRecentContact alert
/// for the machine is resolved during evaluation.
/// </summary>
public sealed class AlertEngine : IAlertEngine
{
    private readonly YarpaDbContext _db;
    private readonly IReadOnlyList<IAlertRule> _rules;
    private readonly AlertOptions _options;
    private readonly ILogger<AlertEngine> _logger;

    public AlertEngine(
        YarpaDbContext db,
        IEnumerable<IAlertRule> rules,
        IOptions<AlertOptions> options,
        ILogger<AlertEngine> logger)
    {
        _db = db;
        _rules = rules.ToList();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AlertEngineResult> EvaluateAsync(
        DiagnosticsSnapshot snapshot,
        MachineEntity machine,
        IReadOnlyList<ChangeEntity> changes,
        CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        List<AlertEntity> openAlerts = await _db.Alerts
            .Where(a => a.MachineId == machine.MachineId && a.State == AlertState.Open)
            .ToListAsync(ct);

        Dictionary<string, List<AlertEntity>> openByType = openAlerts
            .GroupBy(a => a.AlertType)
            .ToDictionary(g => g.Key, g => g.ToList());

        var context = new AlertEvaluationContext(snapshot, machine, changes, _options, now);

        int raised = 0;
        int resolved = 0;

        foreach (IAlertRule rule in _rules)
        {
            AlertRuleResult result = rule.Evaluate(context);
            openByType.TryGetValue(rule.AlertType, out List<AlertEntity>? existing);
            bool hasOpen = existing is { Count: > 0 };

            switch (result.Outcome)
            {
                case AlertRuleOutcome.Raise:
                    if (!hasOpen)
                    {
                        _db.Alerts.Add(new AlertEntity
                        {
                            MachineId = machine.MachineId,
                            AlertType = rule.AlertType,
                            Severity = result.Severity,
                            Message = result.Message,
                            State = AlertState.Open,
                            CreatedAtUtc = now,
                            SourceSnapshotId = snapshot.SnapshotId,
                            SourceChange = result.SourceChange
                        });
                        raised++;
                    }
                    break;

                case AlertRuleOutcome.Clear:
                    if (hasOpen)
                    {
                        foreach (AlertEntity a in existing!)
                        {
                            a.State = AlertState.Resolved;
                            a.ResolvedAtUtc = now;
                            resolved++;
                        }
                    }
                    break;

                case AlertRuleOutcome.Leave:
                default:
                    break;
            }
        }

        // Receiving a snapshot is proof of contact: resolve any open NoRecentContact alert.
        if (openByType.TryGetValue(AlertType.NoRecentContact, out List<AlertEntity>? contactAlerts))
        {
            foreach (AlertEntity a in contactAlerts.Where(a => a.State == AlertState.Open))
            {
                a.State = AlertState.Resolved;
                a.ResolvedAtUtc = now;
                resolved++;
            }
        }

        int openCount = openAlerts.Count(a => a.State == AlertState.Open) + raised;

        _logger.LogInformation(
            "snapshot {SnapshotId}: alerts raised={Raised} resolved={Resolved} open={Open} for machine {MachineId}",
            snapshot.SnapshotId, raised, resolved, openCount, machine.MachineId);

        return new AlertEngineResult
        {
            OpenAlertCount = openCount,
            RaisedCount = raised,
            ResolvedCount = resolved
        };
    }
}
