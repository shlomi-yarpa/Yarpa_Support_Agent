namespace Yarpa.Api.Data.Entities;

/// <summary>
/// String constants for the AlertType discriminator column in the Alerts table.
/// Each constant matches one of the alert rules defined in docs/specification.md §5.
/// </summary>
public static class AlertType
{
    /// <summary>A monitored Yarpa/SQL Windows service is not running.</summary>
    public const string ServiceDown = "ServiceDown";

    /// <summary>Free disk space on at least one drive dropped below the configured threshold.</summary>
    public const string DiskAlmostFull = "DiskAlmostFull";

    /// <summary>A payment terminal that was previously present has disappeared.</summary>
    public const string PaymentTerminalMissing = "PaymentTerminalMissing";

    /// <summary>SQL Server is installed but no instance/service is currently running.</summary>
    public const string SqlNotRunning = "SqlNotRunning";

    /// <summary>The installed Yarpa software version is older than the minimum supported version.</summary>
    public const string OldSoftwareVersion = "OldSoftwareVersion";

    /// <summary>No snapshot has been received from the machine for more than the configured number of days.</summary>
    public const string NoRecentContact = "NoRecentContact";

    /// <summary>A critical section failed to collect on the client (status = error).</summary>
    public const string CollectorError = "CollectorError";
}
