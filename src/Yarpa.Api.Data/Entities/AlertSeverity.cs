namespace Yarpa.Api.Data.Entities;

/// <summary>
/// String constants for the Severity column in the Alerts table.
/// Ordered by urgency: Info &lt; Warning &lt; Critical.
/// </summary>
public static class AlertSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";

    /// <summary>
    /// Returns a numeric rank used for ordering (lower = more urgent).
    /// Critical = 0, Warning = 1, Info = 2, anything else = 3.
    /// </summary>
    public static int Rank(string severity) => severity switch
    {
        Critical => 0,
        Warning => 1,
        Info => 2,
        _ => 3
    };
}
