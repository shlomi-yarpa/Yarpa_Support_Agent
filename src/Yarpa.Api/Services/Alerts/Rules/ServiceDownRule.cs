using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts.Rules;

/// <summary>
/// Raises <see cref="AlertType.ServiceDown"/> when any monitored Windows service
/// (matched against <see cref="AlertOptions.MonitoredServiceNames"/>) is not running.
/// Resolves when all monitored services are running again. If the services section failed
/// to collect (status = error) the rule leaves the current state untouched.
/// </summary>
public sealed class ServiceDownRule : IAlertRule
{
    public string AlertType => Data.Entities.AlertType.ServiceDown;

    public AlertRuleResult Evaluate(AlertEvaluationContext context)
    {
        if (SectionReader.GetStatus(context.Snapshot, "services") == CollectorStatus.Error)
            return AlertRuleResult.Leave(AlertType);

        if (!SectionReader.TryGetData(context.Snapshot, "services", out JsonElement services)
            || services.ValueKind != JsonValueKind.Array)
            return AlertRuleResult.Leave(AlertType);

        string[] monitored = context.Options.MonitoredServiceNames;
        var down = new List<string>();

        foreach (JsonElement svc in services.EnumerateArray())
        {
            string name = SectionReader.Str(svc, "name");
            string exeName = SectionReader.Str(svc, "exeName");
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(exeName))
                continue;

            // Match against the service name OR its backing EXE name (Yarpa services are keyed
            // by EXE name in the field, e.g. Meusensrv.exe / DangotService).
            bool isMonitored = monitored.Any(m =>
                (!string.IsNullOrEmpty(name) && name.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(exeName) && exeName.Contains(m, StringComparison.OrdinalIgnoreCase)));
            if (!isMonitored)
                continue;

            string state = SectionReader.Str(svc, "state");
            if (!string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
            {
                string display = SectionReader.Str(svc, "displayName");
                string label = !string.IsNullOrEmpty(display) ? display
                    : !string.IsNullOrEmpty(name) ? name
                    : exeName;
                down.Add(label);
            }
        }

        if (down.Count == 0)
            return AlertRuleResult.Clear(AlertType);

        string list = string.Join(", ", down);
        string message = down.Count == 1
            ? $"השירות המנוטר '{list}' אינו פעיל."
            : $"שירותים מנוטרים אינם פעילים: {list}.";

        return AlertRuleResult.Raise(AlertType, AlertSeverity.Critical, message);
    }
}
