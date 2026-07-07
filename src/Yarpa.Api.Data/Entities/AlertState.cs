namespace Yarpa.Api.Data.Entities;

/// <summary>
/// String constants for the State column in the Alerts table.
/// Alerts are append-only: a cleared condition transitions an alert from Open to Resolved,
/// it is never deleted.
/// </summary>
public static class AlertState
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}
