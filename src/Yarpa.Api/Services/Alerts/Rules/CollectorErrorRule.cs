using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts.Rules;

/// <summary>
/// Raises <see cref="AlertType.CollectorError"/> when any section listed in
/// <see cref="AlertOptions.CriticalSections"/> reports status = error in the snapshot.
/// Resolves when all critical sections collected successfully (ok or partial).
/// This is the one rule that treats an errored section as a signal rather than ignoring it —
/// the collection failure itself is the alert.
/// </summary>
public sealed class CollectorErrorRule : IAlertRule
{
    public string AlertType => Data.Entities.AlertType.CollectorError;

    public AlertRuleResult Evaluate(AlertEvaluationContext context)
    {
        var failed = new List<string>();

        foreach (string section in context.Options.CriticalSections)
        {
            if (SectionReader.GetStatus(context.Snapshot, section) == CollectorStatus.Error)
                failed.Add(section);
        }

        if (failed.Count == 0)
            return AlertRuleResult.Clear(AlertType);

        string message = $"איסוף נכשל ב-section קריטי: {string.Join(", ", failed)}.";
        return AlertRuleResult.Raise(AlertType, AlertSeverity.Warning, message);
    }
}
