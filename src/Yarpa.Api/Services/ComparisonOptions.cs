namespace Yarpa.Api.Services;

/// <summary>
/// Configurable thresholds used by <see cref="SnapshotComparer"/> when deciding
/// whether a numeric change is significant enough to record.
/// Bind from the "Comparison" section in appsettings.json.
/// </summary>
public sealed class ComparisonOptions
{
    /// <summary>
    /// Minimum absolute change in free-disk percentage that triggers a DiskChanged record.
    /// Default: 5.0 (i.e. a 5 percentage-point swing in free space).
    /// </summary>
    public double DiskFreePercentChangeThreshold { get; set; } = 5.0;
}
