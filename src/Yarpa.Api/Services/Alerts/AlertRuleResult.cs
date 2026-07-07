using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Services.Alerts;

/// <summary>The decision a rule makes for its alert type when evaluated against a snapshot.</summary>
public enum AlertRuleOutcome
{
    /// <summary>The rule has no opinion this time; leave any existing open alert untouched.</summary>
    Leave = 0,

    /// <summary>The condition holds; an open alert of this type should exist.</summary>
    Raise = 1,

    /// <summary>The condition no longer holds; any open alert of this type should be resolved.</summary>
    Clear = 2
}

/// <summary>
/// The result produced by an <see cref="IAlertRule"/> for a single snapshot evaluation.
/// The engine reconciles this against existing open alerts (deduplicating by machine + type).
/// </summary>
public sealed class AlertRuleResult
{
    public string AlertType { get; init; } = string.Empty;
    public AlertRuleOutcome Outcome { get; init; }

    /// <summary>Severity for a raised alert (one of <see cref="AlertSeverity"/>).</summary>
    public string Severity { get; init; } = AlertSeverity.Warning;

    /// <summary>Hebrew message for a raised alert.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Optional change that triggered the alert; links the alert to a specific change record.</summary>
    public ChangeEntity? SourceChange { get; init; }

    public static AlertRuleResult Leave(string alertType) =>
        new() { AlertType = alertType, Outcome = AlertRuleOutcome.Leave };

    public static AlertRuleResult Clear(string alertType) =>
        new() { AlertType = alertType, Outcome = AlertRuleOutcome.Clear };

    public static AlertRuleResult Raise(
        string alertType,
        string severity,
        string message,
        ChangeEntity? sourceChange = null) =>
        new()
        {
            AlertType = alertType,
            Outcome = AlertRuleOutcome.Raise,
            Severity = severity,
            Message = message,
            SourceChange = sourceChange
        };
}
