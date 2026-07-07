namespace Yarpa.Api.Services.Retention;

/// <summary>Outcome of a single retention run.</summary>
public sealed record RetentionResult
{
    /// <summary>Whether the policy is enabled. When false nothing is scanned or deleted.</summary>
    public bool Enabled { get; init; }

    /// <summary>UTC cutoff — snapshots collected before this instant are deletion candidates.</summary>
    public DateTime CutoffUtc { get; init; }

    /// <summary>Snapshots older than the cutoff (before applying safety rules).</summary>
    public int OlderThanCutoff { get; init; }

    /// <summary>Old snapshots kept because they are protected (last / referenced / within the newest N).</summary>
    public int Protected { get; init; }

    /// <summary>Snapshots actually deleted in this run.</summary>
    public int Deleted { get; init; }
}

/// <summary>
/// Prunes old raw snapshots according to <see cref="RetentionOptions"/> while preserving
/// referential integrity and all historical Changes/Alerts.
/// </summary>
public interface IRetentionService
{
    Task<RetentionResult> RunAsync(CancellationToken ct);
}
