using System.Globalization;
using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts.Rules;

/// <summary>
/// Raises <see cref="AlertType.DiskAlmostFull"/> when any disk's free space drops below the
/// configured percentage (<see cref="AlertOptions.MinFreeDiskPercent"/>) or absolute GB
/// (<see cref="AlertOptions.MinFreeDiskGb"/>). Resolves when all disks are above both
/// thresholds. Leaves state untouched if the disks section failed to collect.
/// </summary>
public sealed class DiskAlmostFullRule : IAlertRule
{
    public string AlertType => Data.Entities.AlertType.DiskAlmostFull;

    public AlertRuleResult Evaluate(AlertEvaluationContext context)
    {
        if (SectionReader.GetStatus(context.Snapshot, "disks") == CollectorStatus.Error)
            return AlertRuleResult.Leave(AlertType);

        if (!SectionReader.TryGetData(context.Snapshot, "disks", out JsonElement disks)
            || disks.ValueKind != JsonValueKind.Array)
            return AlertRuleResult.Leave(AlertType);

        double minPercent = context.Options.MinFreeDiskPercent;
        double minGb = context.Options.MinFreeDiskGb;

        var low = new List<string>();

        foreach (JsonElement disk in disks.EnumerateArray())
        {
            string drive = SectionReader.Str(disk, "drive");
            bool hasPercent = SectionReader.TryGetDouble(disk, "freePercent", out double freePercent);
            bool hasGb = SectionReader.TryGetDouble(disk, "freeGb", out double freeGb);

            bool belowPercent = hasPercent && freePercent < minPercent;
            bool belowGb = hasGb && freeGb < minGb;

            if (belowPercent || belowGb)
            {
                string pct = hasPercent ? freePercent.ToString("0.#", CultureInfo.InvariantCulture) : "?";
                string gb = hasGb ? freeGb.ToString("0.#", CultureInfo.InvariantCulture) : "?";
                low.Add($"{drive} ({pct}% / {gb}GB פנויים)");
            }
        }

        if (low.Count == 0)
            return AlertRuleResult.Clear(AlertType);

        string message = $"מקום פנוי נמוך בכונן: {string.Join(", ", low)}.";
        return AlertRuleResult.Raise(AlertType, AlertSeverity.Warning, message);
    }
}
