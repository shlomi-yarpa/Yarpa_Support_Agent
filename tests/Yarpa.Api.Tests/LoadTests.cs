using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Yarpa.Api.Tests.Infrastructure;
using Yarpa.Contracts;
using Xunit.Abstractions;

namespace Yarpa.Api.Tests;

/// <summary>
/// Basic load test for POST /api/v1/snapshots. Sends many concurrent snapshots whose
/// payload is the real snapshot.json (a representative full collection), each with a fresh
/// snapshotId and a distinct machineId, and asserts that every request is accepted with no
/// server-side failures. Throughput is written to the test output for documentation.
///
/// Note: the integration test uses the in-memory database, so absolute throughput is not
/// representative of production SQL Server. It exists to prove correctness under concurrency.
/// A production HTTP load run is provided by scripts/loadtest-snapshots.ps1.
/// </summary>
[Collection("API Integration Tests")]
public class LoadTests
{
    private const int TotalRequests = 100;
    private const int MaxConcurrency = 10;

    private readonly ITestOutputHelper _output;

    public LoadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Post_ConcurrentSnapshots_AllAcceptedWithoutFailures()
    {
        string template = LoadSnapshotJsonTemplate();
        int payloadBytes = Encoding.UTF8.GetByteCount(template);

        // Own factory instance → isolated in-memory database, so the bulk of load-test
        // machines never pollute the shared fixture used by the other integration tests.
        using var factory = new TestApiFactory();
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        using var throttle = new SemaphoreSlim(MaxConcurrency);
        var statuses = new HttpStatusCode[TotalRequests];

        var sw = Stopwatch.StartNew();

        IEnumerable<Task> tasks = Enumerable.Range(0, TotalRequests).Select(async i =>
        {
            await throttle.WaitAsync();
            try
            {
                string json = BuildUniquePayload(template, i);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage response =
                    await client.PostAsync("/api/v1/snapshots", content);
                statuses[i] = response.StatusCode;
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        int accepted = statuses.Count(s => s == HttpStatusCode.Accepted);
        int failures = statuses.Count(s => (int)s >= 500);
        double perSecond = TotalRequests / Math.Max(0.001, sw.Elapsed.TotalSeconds);

        _output.WriteLine($"Load test: {TotalRequests} requests, payload ~{payloadBytes / 1024.0:F1} KB each");
        _output.WriteLine($"  Concurrency : {MaxConcurrency}");
        _output.WriteLine($"  Elapsed     : {sw.Elapsed.TotalSeconds:F2} s");
        _output.WriteLine($"  Throughput  : {perSecond:F1} req/s");
        _output.WriteLine($"  Accepted    : {accepted}/{TotalRequests}");
        _output.WriteLine($"  5xx failures: {failures}");

        Assert.Equal(0, failures);
        Assert.Equal(TotalRequests, accepted);
    }

    /// <summary>
    /// Loads snapshot.json from the repository root (searched upward from the test binaries)
    /// and normalises it through the contract type so the payload matches the real schema.
    /// Falls back to a synthetic payload if the file cannot be located.
    /// </summary>
    private static string LoadSnapshotJsonTemplate()
    {
        string? path = FindRepoFile("snapshot.json");
        if (path != null)
        {
            string raw = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            DiagnosticsSnapshot? snapshot =
                JsonSerializer.Deserialize<DiagnosticsSnapshot>(raw, options);
            if (snapshot != null)
                return JsonSerializer.Serialize(snapshot);
        }

        // Fallback: a minimal but valid snapshot.
        var fallback = new DiagnosticsSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            AgentVersion = "1.0.0",
            MachineId = "load-template",
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Sections = new Dictionary<string, SnapshotSection>
            {
                ["system"] = SnapshotSection.FromData(new { computerName = "LOAD-PC" })
            }
        };
        return JsonSerializer.Serialize(fallback);
    }

    private static string BuildUniquePayload(string template, int index)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        DiagnosticsSnapshot snapshot =
            JsonSerializer.Deserialize<DiagnosticsSnapshot>(template, options)!;

        var clone = new DiagnosticsSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            SchemaVersion = snapshot.SchemaVersion,
            AgentVersion = snapshot.AgentVersion,
            MachineId = $"load-machine-{index:D4}",
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Sections = snapshot.Sections
        };
        return JsonSerializer.Serialize(clone);
    }

    private static string? FindRepoFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
