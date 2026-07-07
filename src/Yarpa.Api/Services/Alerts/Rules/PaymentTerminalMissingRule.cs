using System.Text.Json;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Services.Alerts.Rules;

/// <summary>
/// Raises <see cref="AlertType.PaymentTerminalMissing"/> when the comparer detects that a
/// payment terminal was removed (a DeviceRemoved change in the paymentTerminals section).
/// Resolves when a terminal reappears (a DeviceAdded change) and nothing was removed in the
/// same snapshot. Otherwise leaves any existing alert untouched — the removal is an event, so
/// the alert must stay open across later snapshots until the terminal comes back.
/// Because the comparer skips sections with error status, a failed collection never produces a
/// spurious "removed" change here.
/// </summary>
public sealed class PaymentTerminalMissingRule : IAlertRule
{
    private const string Section = "paymentTerminals";

    public string AlertType => Data.Entities.AlertType.PaymentTerminalMissing;

    public AlertRuleResult Evaluate(AlertEvaluationContext context)
    {
        ChangeEntity? removed = null;
        bool added = false;

        foreach (ChangeEntity change in context.Changes)
        {
            if (!string.Equals(change.SectionName, Section, StringComparison.Ordinal))
                continue;

            if (change.ChangeType == ChangeType.DeviceRemoved)
                removed ??= change;
            else if (change.ChangeType == ChangeType.DeviceAdded)
                added = true;
        }

        if (removed is not null)
        {
            string label = DescribeTerminal(removed.OldValue);
            string message = $"מסוף סליקה נותק או נעלם: {label}.";
            return AlertRuleResult.Raise(AlertType, AlertSeverity.Critical, message, removed);
        }

        if (added)
            return AlertRuleResult.Clear(AlertType);

        return AlertRuleResult.Leave(AlertType);
    }

    /// <summary>Builds a short human label from the removed terminal's stored JSON value.</summary>
    private static string DescribeTerminal(string? oldValueJson)
    {
        if (string.IsNullOrWhiteSpace(oldValueJson))
            return "מסוף לא ידוע";

        try
        {
            using JsonDocument doc = JsonDocument.Parse(oldValueJson);
            JsonElement el = doc.RootElement;
            string vendor = SectionReader.Str(el, "vendor");
            string model = SectionReader.Str(el, "model");
            string comPort = SectionReader.Str(el, "comPort");

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(vendor)) parts.Add(vendor);
            if (!string.IsNullOrEmpty(model)) parts.Add(model);
            string label = parts.Count > 0 ? string.Join(" ", parts) : "מסוף לא ידוע";
            if (!string.IsNullOrEmpty(comPort))
                label += $" ({comPort})";
            return label;
        }
        catch (JsonException)
        {
            return "מסוף לא ידוע";
        }
    }
}
