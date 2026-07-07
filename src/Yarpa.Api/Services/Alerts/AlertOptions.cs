namespace Yarpa.Api.Services.Alerts;

/// <summary>
/// Configurable thresholds used by the alert engine and its rules.
/// Bind from the "Alerts" section in appsettings.json. No thresholds are hard-coded
/// in the rules themselves — every value that decides whether an alert fires is here.
/// </summary>
public sealed class AlertOptions
{
    /// <summary>
    /// A disk is considered "almost full" when its free percentage is below this value.
    /// Default: 10 (percent).
    /// </summary>
    public double MinFreeDiskPercent { get; set; } = 10.0;

    /// <summary>
    /// A disk is also considered "almost full" when its absolute free space (GB) is below this value.
    /// Either condition (percent OR GB) triggers the alert. Default: 5 (GB).
    /// </summary>
    public double MinFreeDiskGb { get; set; } = 5.0;

    /// <summary>
    /// Minimum supported Yarpa software version. A reported version older than this raises
    /// OldSoftwareVersion. Parsed with <see cref="System.Version"/>. Default: "8.0.0".
    /// </summary>
    public string MinSupportedYarpaVersion { get; set; } = "8.0.0";

    /// <summary>
    /// Number of days without a received snapshot after which NoRecentContact is raised.
    /// Default: 3.
    /// </summary>
    public int NoRecentContactDays { get; set; } = 3;

    /// <summary>
    /// How often (minutes) the background no-recent-contact checker runs. Default: 60.
    /// </summary>
    public int NoRecentContactScanIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Service names (case-insensitive substring match) that are considered monitored
    /// for the ServiceDown rule. Default: SQL Server + Yarpa services.
    /// </summary>
    public string[] MonitoredServiceNames { get; set; } =
        { "MSSQLSERVER", "SQLSERVERAGENT", "Yarpa" };

    /// <summary>
    /// Sections that are considered critical for the CollectorError rule. A section listed
    /// here with status = error raises a CollectorError alert. Default: core diagnostics sections.
    /// </summary>
    public string[] CriticalSections { get; set; } =
        { "system", "os", "disks", "services", "sqlServer" };
}
