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
/// </summary>
public sealed class ServiceOptions
{
    /// <summary>Interval in hours between collection cycles (default 6).</summary>
    public double IntervalHours { get; init; } = 6;

    /// <summary>Collect once immediately when the service starts (default true).</summary>
    public bool RunImmediatelyOnStart { get; init; } = true;
}
