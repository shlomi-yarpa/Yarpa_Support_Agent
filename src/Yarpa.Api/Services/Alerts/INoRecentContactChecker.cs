namespace Yarpa.Api.Services.Alerts;

/// <summary>Outcome of a no-recent-contact scan.</summary>
public sealed class NoRecentContactResult
{
    /// <summary>Number of machines newly flagged as out of contact.</summary>
    public int RaisedCount { get; init; }

    /// <summary>Number of NoRecentContact alerts resolved because the machine is back in contact.</summary>
    public int ResolvedCount { get; init; }

    /// <summary>Total machines examined.</summary>
    public int ScannedMachines { get; init; }
}

/// <summary>
/// Time-based check that flags machines which have not sent a snapshot for longer than the
/// configured threshold (<see cref="AlertOptions.NoRecentContactDays"/>). Unlike the other
/// rules this is not derived from a received snapshot, so it runs on demand (internal endpoint)
/// or periodically (hosted background service).
/// </summary>
public interface INoRecentContactChecker
{
    Task<NoRecentContactResult> RunAsync(CancellationToken ct);
}
