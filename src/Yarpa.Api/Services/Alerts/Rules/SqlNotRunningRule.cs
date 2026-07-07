using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts.Rules;

/// <summary>
/// Raises <see cref="AlertType.SqlNotRunning"/> when SQL Server is installed but no instance
/// is running. Resolves when SQL is not installed or at least one instance is running.
/// If the sqlServer section failed to collect (status = error) the rule leaves state untouched
/// so that a collection failure is never misread as "SQL not running".
/// </summary>
public sealed class SqlNotRunningRule : IAlertRule
{
    public string AlertType => Data.Entities.AlertType.SqlNotRunning;

    public AlertRuleResult Evaluate(AlertEvaluationContext context)
    {
        if (SectionReader.GetStatus(context.Snapshot, "sqlServer") == CollectorStatus.Error)
            return AlertRuleResult.Leave(AlertType);

        if (!SectionReader.TryGetData(context.Snapshot, "sqlServer", out JsonElement sql)
            || sql.ValueKind != JsonValueKind.Object)
            return AlertRuleResult.Leave(AlertType);

        bool installed = SectionReader.BoolProp(sql, "installed");
        if (!installed)
            return AlertRuleResult.Clear(AlertType);

        if (!sql.TryGetProperty("instances", out JsonElement instances)
            || instances.ValueKind != JsonValueKind.Array
            || instances.GetArrayLength() == 0)
        {
            // Installed but no instance information at all → treat as not running.
            return AlertRuleResult.Raise(
                AlertType,
                AlertSeverity.Critical,
                "SQL Server מותקן אך לא נמצא Instance פעיל.");
        }

        var stopped = new List<string>();
        bool anyRunning = false;

        foreach (JsonElement inst in instances.EnumerateArray())
        {
            string state = SectionReader.Str(inst, "serviceState");
            if (string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
            {
                anyRunning = true;
            }
            else
            {
                string name = SectionReader.Str(inst, "name");
                stopped.Add(string.IsNullOrEmpty(name) ? "(instance)" : name);
            }
        }

        if (anyRunning)
            return AlertRuleResult.Clear(AlertType);

        string message = $"SQL Server מותקן אך השירות אינו פעיל (Instances: {string.Join(", ", stopped)}).";
        return AlertRuleResult.Raise(AlertType, AlertSeverity.Critical, message);
    }
}
