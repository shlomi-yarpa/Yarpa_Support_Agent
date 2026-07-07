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
    /// Minimum supported Yarpa (Piryon) build number. A reported build below this raises
    /// OldSoftwareVersion (severity Warning — all versions are supported, this is advisory only).
    /// The build is the last dotted segment of the version (e.g. "1.0.898.10235" ⇒ 10235).
    /// Default: 10300.
    /// </summary>
    public int MinSupportedYarpaBuild { get; set; } = 10300;

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
    /// Service identifiers (case-insensitive substring match against the service name OR its
    /// backing EXE name) considered monitored for the ServiceDown rule. Default: SQL Server and
    /// the optional Yarpa services (Meusensrv, PirReplMercaz2SnifService, DangotService).
    /// These Yarpa services are optional: they only trigger an alert when installed but stopped —
    /// a machine without them simply reports no such service, so nothing fires.
    /// </summary>
    public string[] MonitoredServiceNames { get; set; } =
        { "MSSQLSERVER", "SQLSERVERAGENT", "Yarpa", "Meusensrv", "PirReplMercaz2SnifService", "DangotService" };

    /// <summary>
    /// Sections that are considered critical for the CollectorError rule. A section listed
    /// here with status = error raises a CollectorError alert. Default: core diagnostics sections.
    /// </summary>
    public string[] CriticalSections { get; set; } =
        { "system", "os", "disks", "services", "sqlServer" };
}
