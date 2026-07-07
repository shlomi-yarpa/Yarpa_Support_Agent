using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts.Rules;

/// <summary>
/// Raises <see cref="AlertType.OldSoftwareVersion"/> when the reported Yarpa (Piryon) build is
/// below <see cref="AlertOptions.MinSupportedYarpaBuild"/>. All versions are supported, so this
/// is a Warning (advisory) only. Resolves when the build is at or above the threshold. Leaves
/// state untouched when no parseable build is available (unknown) or the section errored.
///
/// The build is the last dotted segment of the version (e.g. "1.0.898.10235" ⇒ 10235); if the
/// snapshot already carries an explicit numeric "build" field it is preferred.
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

        int? build = ResolveBuild(yv);
        if (build is null)
            return AlertRuleResult.Leave(AlertType);

        if (build.Value >= context.Options.MinSupportedYarpaBuild)
            return AlertRuleResult.Clear(AlertType);

        string versionText = SectionReader.Str(yv, "version");
        string versionLabel = string.IsNullOrWhiteSpace(versionText) ? build.Value.ToString() : versionText;
        string message =
            $"גרסת תוכנת Piryon (build {build.Value}, {versionLabel}) ישנה מהמינימום המומלץ (build {context.Options.MinSupportedYarpaBuild}).";
        return AlertRuleResult.Raise(AlertType, AlertSeverity.Warning, message);
    }

    /// <summary>
    /// Resolves the build number: prefers an explicit numeric "build" field, otherwise parses
    /// the last dotted segment of the "version" string. Returns null when neither is available.
    /// </summary>
    private static int? ResolveBuild(JsonElement yv)
    {
        if (yv.TryGetProperty("build", out JsonElement buildEl))
        {
            if (buildEl.ValueKind == JsonValueKind.Number && buildEl.TryGetInt32(out int b))
                return b;
            if (buildEl.ValueKind == JsonValueKind.String
                && int.TryParse(buildEl.GetString(), out int bs))
                return bs;
        }

        string versionText = SectionReader.Str(yv, "version");
        return ParseBuildFromVersion(versionText);
    }

    /// <summary>Returns the last dotted numeric segment of a version string, or null.</summary>
    private static int? ParseBuildFromVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        string last = version.Trim().Split('.').Last();
        return int.TryParse(last, out int b) ? b : null;
    }
}
