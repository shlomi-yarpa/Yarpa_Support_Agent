namespace Yarpa.Agent.Collectors;

/// <summary>
/// A single, self-contained information collector. Each implementation produces
/// exactly one section of the diagnostics model. Collectors are added or removed
/// via DI registration only, without touching the orchestrator. See docs/collectors.md.
/// </summary>
public interface ICollector
{
    /// <summary>Name of the section this collector produces (e.g. "os", "disks").</summary>
    string SectionName { get; }

    /// <summary>
    /// Collects the section. Implementations must isolate failures: catch exceptions
    /// and return a result with <see cref="CollectorStatus.Error"/> instead of throwing.
    /// </summary>
    Task<CollectorResult> CollectAsync(CancellationToken ct);
}
