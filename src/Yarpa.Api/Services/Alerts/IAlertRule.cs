namespace Yarpa.Api.Services.Alerts;

/// <summary>
/// A single, self-contained alert rule. Each rule owns exactly one <see cref="AlertType"/>
/// and decides — based on the snapshot, its changes and configured thresholds — whether an
/// alert of that type should be raised, cleared, or left unchanged. Rules are registered in
/// DI so that adding or removing a rule does not require changing the engine.
/// </summary>
public interface IAlertRule
{
    /// <summary>The alert type this rule owns (one of <see cref="Data.Entities.AlertType"/>).</summary>
    string AlertType { get; }

    /// <summary>Evaluates the rule against the current snapshot context.</summary>
    AlertRuleResult Evaluate(AlertEvaluationContext context);
}
