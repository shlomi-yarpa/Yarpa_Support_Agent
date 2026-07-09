namespace Yarpa.Api.Services;

/// <summary>
/// Security-related, configurable limits. Bound from the "Security" section of
/// appsettings.json. No secrets live here — only operational safety limits.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Maximum accepted request body size in bytes. Requests larger than this are rejected
    /// with HTTP 413. Default 5 MB — comfortably above a realistic snapshot payload.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Enforce HTTPS (HSTS + redirection) in non-Development environments. Set to false for
    /// deployments on a trusted, closed private network that intentionally serve plain HTTP.
    /// Default true.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>Basic per-API-key rate-limiting settings.</summary>
    public RateLimitOptions RateLimit { get; set; } = new();
}

/// <summary>
/// Fixed-window rate-limiting configuration applied to the versioned API endpoints,
/// partitioned by API key (falling back to remote IP when the key is absent).
/// Exceeding the window returns HTTP 429.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Requests permitted per window per partition. Default 10000.</summary>
    public int PermitPerWindow { get; set; } = 10_000;

    /// <summary>Window length in seconds. Default 60.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Maximum queued requests once the limit is reached. Default 0 (reject immediately).</summary>
    public int QueueLimit { get; set; } = 0;
}
