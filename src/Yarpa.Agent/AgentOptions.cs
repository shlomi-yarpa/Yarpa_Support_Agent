namespace Yarpa.Agent;

/// <summary>
/// Strongly-typed Agent configuration bound from the "Agent" section of appsettings.json.
/// Secrets (ApiKey, connection strings) are supplied via environment variables or a local
/// appsettings.json that is not committed to source control.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Base URL of the REST API, e.g. https://localhost:5001. HTTPS only.</summary>
    public string ApiBaseUrl { get; init; } = string.Empty;

    /// <summary>Per-customer API key sent in the X-Api-Key header. Never commit real values.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Number of retry attempts on transient HTTP failures (default 3).</summary>
    public int RetryCount { get; init; } = 3;

    /// <summary>Base delay in seconds for exponential backoff (default 2 → 2s, 4s, 8s).</summary>
    public int RetryBaseDelaySeconds { get; init; } = 2;

    /// <summary>
    /// Directory for the offline snapshot queue.
    /// Defaults to %LocalAppData%\Yarpa\OfflineQueue when empty.
    /// </summary>
    public string OfflineQueuePath { get; init; } = string.Empty;

    /// <summary>Windows Service scheduling options (used only in --service mode).</summary>
    public ServiceOptions Service { get; init; } = new();
}

/// <summary>
/// Scheduling configuration for the Agent when it runs as a long-lived Windows Service.
/// Ignored by the one-shot CLI modes (--once / --dry-run / --output).
///
/// Default cadence: one collection per week, during a quiet night-time window on the
/// client machine's local clock. A random time inside the window is chosen for each run
/// so that many machines do not hit the API simultaneously.
/// On-demand collection (technician-initiated) and the first collection at install time
/// are handled separately (--once and <see cref="RunImmediatelyOnStart"/>).
/// </summary>
public sealed class ServiceOptions
{
    /// <summary>Number of days between scheduled collections (default 7 = weekly).</summary>
    public int IntervalDays { get; init; } = 7;

    /// <summary>
    /// Start hour (inclusive, local time, 0-23) of the preferred night-time window.
    /// Default 2 (02:00).
    /// </summary>
    public int PreferredHourStart { get; init; } = 2;

    /// <summary>
    /// End hour (exclusive, local time, 0-23) of the preferred night-time window.
    /// Default 4 (up to 03:59).
    /// </summary>
    public int PreferredHourEnd { get; init; } = 4;

    /// <summary>
    /// Optional fixed-interval override in hours. When greater than 0, the day/window
    /// schedule is ignored and the Agent simply collects every N hours. Intended for
    /// testing or special deployments (default 0 = disabled, use the weekly night schedule).
    /// </summary>
    public double IntervalHours { get; init; }

    /// <summary>Collect once immediately when the service starts, e.g. at install (default true).</summary>
    public bool RunImmediatelyOnStart { get; init; } = true;
}
