using System.Net;
using System.Text;
using System.Text.Json;
using Yarpa.Api.Tests.Infrastructure;
using Yarpa.Contracts;

namespace Yarpa.Api.Tests;

[Collection("API Integration Tests")]
public class SnapshotsEndpointTests
{
    private readonly TestApiFactory _factory;

    public SnapshotsEndpointTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static StringContent BuildSnapshotContent(Guid? snapshotId = null, string? machineId = null)
    {
        var snapshot = new DiagnosticsSnapshot
        {
            SnapshotId = snapshotId ?? Guid.NewGuid(),
            AgentVersion = "1.0.0",
            MachineId = machineId ?? "test-machine-001",
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Sections = new Dictionary<string, SnapshotSection>
            {
                ["system"] = SnapshotSection.FromData(new { computerName = "TEST-PC" }),
                ["os"] = SnapshotSection.FromData(new { caption = "Windows 11 Pro", build = "22631" })
            }
        };

        string json = JsonSerializer.Serialize(snapshot);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_WithNoApiKey_Returns401()
    {
        HttpClient client = _factory.CreateClient();
        using var content = BuildSnapshotContent();

        HttpResponseMessage response = await client.PostAsync("/api/v1/snapshots", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithWrongApiKey_Returns401()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "this-is-a-wrong-key");
        using var content = BuildSnapshotContent();

        HttpResponseMessage response = await client.PostAsync("/api/v1/snapshots", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithValidSnapshot_Returns202()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);
        using var content = BuildSnapshotContent(machineId: "machine-202-test");

        HttpResponseMessage response = await client.PostAsync("/api/v1/snapshots", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("snapshotId", body);
        Assert.Contains("machineId", body);
    }

    [Fact]
    public async Task Post_SameSnapshotIdTwice_SecondCallReturns200()
    {
        var snapshotId = Guid.NewGuid();
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        using var content1 = BuildSnapshotContent(snapshotId: snapshotId, machineId: "idempotency-machine");
        HttpResponseMessage first = await client.PostAsync("/api/v1/snapshots", content1);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        using var content2 = BuildSnapshotContent(snapshotId: snapshotId, machineId: "idempotency-machine");
        HttpResponseMessage second = await client.PostAsync("/api/v1/snapshots", content2);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Post_WithMissingMachineId_Returns400()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);
        using var content = BuildSnapshotContent(machineId: "");

        HttpResponseMessage response = await client.PostAsync("/api/v1/snapshots", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithInvalidJson_Returns400()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);
        using var content = new StringContent("{ this is not valid JSON", Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync("/api/v1/snapshots", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_DoesNotRequireApiKey()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
