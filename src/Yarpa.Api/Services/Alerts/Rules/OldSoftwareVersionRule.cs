using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts.Rules;

/// <summary>
/// Raises <see cref="AlertType.OldSoftwareVersion"/> when the reported Yarpa version is older
/// than <see cref="AlertOptions.MinSupportedYarpaVersion"/>. Resolves when the version is at or
/// above the minimum. Leaves state untouched when no parseable version is available (unknown).
/// </summary>
public sealed class OldSoftwareVersionRule : IAlertRule
{
    public string AlertType => Data.Entities.AlertType.OldSoftwareVersion;

    public AlertRuleResult Evaluate(AlertEvaluationContext context)
    {
        if (SectionReader.GetStatus(context.Snapshot, "yarpaVersion") == CollectorStatus.Error)
            return AlertRuleResult.Leave(AlertType);

        if (!SectionReader.TryGetData(context.Snapshot, "yarpaVersion", out JsonElement yv)
            || yv.ValueKind != JsonValueKind.Object)
            return AlertRuleResult.Leave(AlertType);

        string versionText = SectionReader.Str(yv, "version");
        if (string.IsNullOrWhiteSpace(versionText) || !TryParseVersion(versionText, out Version current))
            return AlertRuleResult.Leave(AlertType);

        if (!TryParseVersion(context.Options.MinSupportedYarpaVersion, out Version minimum))
            return AlertRuleResult.Leave(AlertType);

        if (current >= minimum)
            return AlertRuleResult.Clear(AlertType);

        string message =
            $"גרסת תוכנת Yarpa ({versionText}) ישנה מהמינימום הנתמך ({context.Options.MinSupportedYarpaVersion}).";
        return AlertRuleResult.Raise(AlertType, AlertSeverity.Warning, message);
    }

    /// <summary>
    /// Parses a dotted version string, normalising a bare "8" or "8.4" so that
    /// <see cref="System.Version"/> can compare it (it requires at least major.minor).
    /// </summary>
    private static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0);
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;

        // Keep only leading numeric-dotted portion (drop suffixes like "-beta").
        int end = 0;
        while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] == '.'))
            end++;
        trimmed = trimmed[..end].Trim('.');
        if (trimmed.Length == 0)
            return false;

        if (!trimmed.Contains('.'))
            trimmed += ".0";

        return Version.TryParse(trimmed, out version!);
    }
}
