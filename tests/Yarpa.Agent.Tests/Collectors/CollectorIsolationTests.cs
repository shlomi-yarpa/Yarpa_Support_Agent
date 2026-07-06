using Microsoft.Extensions.Logging.Abstractions;
using Yarpa.Agent;
using Yarpa.Agent.Collectors;
using Yarpa.Contracts;

namespace Yarpa.Agent.Tests.Collectors;

/// <summary>
/// Verifies that the CollectionOrchestrator isolates individual collector failures:
/// one failing collector must not prevent other collectors from producing results,
/// and the failing section must be marked with status=error.
/// </summary>
public class CollectorIsolationTests
{
    // ── Fake collectors ──────────────────────────────────────────────────────

    private sealed class AlwaysOkCollector(string sectionName) : ICollector
    {
        public string SectionName => sectionName;

        public Task<CollectorResult> CollectAsync(CancellationToken ct) =>
            Task.FromResult(CollectorResult.Ok(
                sectionName,
                data: new { value = "ok" },
                durationMs: 1));
    }

    private sealed class AlwaysThrowCollector(string sectionName) : ICollector
    {
        public string SectionName => sectionName;

        public Task<CollectorResult> CollectAsync(CancellationToken ct) =>
            throw new InvalidOperationException("simulated collector crash");
    }

    private sealed class AlwaysErrorCollector(string sectionName) : ICollector
    {
        public string SectionName => sectionName;

        public Task<CollectorResult> CollectAsync(CancellationToken ct) =>
            Task.FromResult(CollectorResult.Failed(
                sectionName,
                error: "simulated error result",
                durationMs: 1));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CollectionOrchestrator BuildOrchestrator(params ICollector[] collectors)
    {
        var machineIdentity = new MachineIdentity();
        var logger = NullLogger<CollectionOrchestrator>.Instance;
        return new CollectionOrchestrator(collectors, machineIdentity, logger);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orchestrator_WhenOneCollectorThrows_OtherSectionsAreOk()
    {
        var orchestrator = BuildOrchestrator(
            new AlwaysOkCollector("section-a"),
            new AlwaysThrowCollector("section-crash"),
            new AlwaysOkCollector("section-b"));

        DiagnosticsSnapshot snapshot = await orchestrator.CollectAsync(CancellationToken.None);

        Assert.Equal(CollectorStatus.Ok, snapshot.Sections["section-a"].Status);
        Assert.Equal(CollectorStatus.Ok, snapshot.Sections["section-b"].Status);
        Assert.Equal(CollectorStatus.Error, snapshot.Sections["section-crash"].Status);
        Assert.NotNull(snapshot.Sections["section-crash"].Error);
    }

    [Fact]
    public async Task Orchestrator_WhenCollectorReturnsError_SectionIsError()
    {
        var orchestrator = BuildOrchestrator(
            new AlwaysOkCollector("good"),
            new AlwaysErrorCollector("bad"));

        DiagnosticsSnapshot snapshot = await orchestrator.CollectAsync(CancellationToken.None);

        Assert.Equal(CollectorStatus.Ok, snapshot.Sections["good"].Status);
        Assert.Equal(CollectorStatus.Error, snapshot.Sections["bad"].Status);
        Assert.Equal("simulated error result", snapshot.Sections["bad"].Error);
    }

    [Fact]
    public async Task Orchestrator_Snapshot_HasRequiredMetadata()
    {
        var orchestrator = BuildOrchestrator(new AlwaysOkCollector("os"));

        DiagnosticsSnapshot snapshot = await orchestrator.CollectAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, snapshot.SnapshotId);
        Assert.NotEmpty(snapshot.MachineId);
        Assert.NotEmpty(snapshot.AgentVersion);
        Assert.NotEqual(default, snapshot.CollectedAtUtc);
        Assert.Equal(DiagnosticsSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
    }

    [Fact]
    public async Task Orchestrator_EachRun_ProducesUniquSnapshotId()
    {
        var orchestrator = BuildOrchestrator(new AlwaysOkCollector("os"));

        DiagnosticsSnapshot s1 = await orchestrator.CollectAsync(CancellationToken.None);
        DiagnosticsSnapshot s2 = await orchestrator.CollectAsync(CancellationToken.None);

        Assert.NotEqual(s1.SnapshotId, s2.SnapshotId);
    }
}
