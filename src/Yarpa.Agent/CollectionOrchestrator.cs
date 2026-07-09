using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarpa.Agent.Collectors;
using Yarpa.Contracts;

namespace Yarpa.Agent;

/// <summary>
/// Runs all registered <see cref="ICollector"/> instances in parallel, isolates
/// individual failures, and assembles the <see cref="DiagnosticsSnapshot"/>.
/// A collector that throws is recorded as an Error section; it never propagates.
/// </summary>
public sealed class CollectionOrchestrator
{
    private readonly IEnumerable<ICollector> _collectors;
    private readonly MachineIdentity _machineIdentity;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<CollectionOrchestrator> _logger;

    private static readonly string AgentVersion =
        System.Reflection.Assembly.GetEntryAssembly()
            ?.GetName().Version?.ToString(3) ?? "1.0.0";

    public CollectionOrchestrator(
        IEnumerable<ICollector> collectors,
        MachineIdentity machineIdentity,
        IOptions<AgentOptions> agentOptions,
        ILogger<CollectionOrchestrator> logger)
    {
        _collectors = collectors;
        _machineIdentity = machineIdentity;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    public async Task<DiagnosticsSnapshot> CollectAsync(CancellationToken ct)
    {
        _logger.LogInformation("starting collection with {Count} collector(s)", _collectors.Count());

        var tasks = _collectors
            .Select(c => RunSafeAsync(c, ct))
            .ToList();

        CollectorResult[] results = await Task.WhenAll(tasks);

        var sections = results.ToDictionary(r => r.SectionName, r => r.ToSection());

        string? siteCustomerCode = string.IsNullOrWhiteSpace(_agentOptions.SiteCustomerCode)
            ? null
            : _agentOptions.SiteCustomerCode.Trim();

        var snapshot = new DiagnosticsSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            AgentVersion = AgentVersion,
            MachineId = _machineIdentity.MachineId,
            SiteCustomerCode = siteCustomerCode,
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Sections = sections
        };

        int errors = results.Count(r => r.Status == Yarpa.Contracts.CollectorStatus.Error);
        int partials = results.Count(r => r.Status == Yarpa.Contracts.CollectorStatus.Partial);
        _logger.LogInformation(
            "collection complete: snapshotId={SnapshotId} sections={Total} errors={Errors} partials={Partials}",
            snapshot.SnapshotId, results.Length, errors, partials);

        return snapshot;
    }

    private async Task<CollectorResult> RunSafeAsync(ICollector collector, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("collector {SectionName}: starting", collector.SectionName);
            CollectorResult result = await collector.CollectAsync(ct);

            if (result.Status == Yarpa.Contracts.CollectorStatus.Error)
                _logger.LogWarning("collector {SectionName}: error – {Error}",
                    collector.SectionName, result.Error);
            else
                _logger.LogDebug("collector {SectionName}: done in {DurationMs}ms (status={Status})",
                    collector.SectionName, result.DurationMs, result.Status);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "collector {SectionName} threw an unhandled exception",
                collector.SectionName);
            return CollectorResult.Failed(collector.SectionName, ex.Message, durationMs: 0);
        }
    }
}
