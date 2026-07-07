using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Yarpa.Api.Tests.Infrastructure;
using Yarpa.Contracts;

namespace Yarpa.Api.Tests;

/// <summary>
/// Stage 6 hardening: readiness probe, metrics endpoint, payload-size limit (413)
/// and per-API-key rate limiting (429).
/// </summary>
[Collection("API Integration Tests")]
public class HardeningEndpointTests
{
    private readonly TestApiFactory _factory;

    public HardeningEndpointTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    private static StringContent BuildSnapshotContent(string machineId)
    {
        var snapshot = new DiagnosticsSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            AgentVersion = "1.0.0",
            MachineId = machineId,
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Sections = new Dictionary<string, SnapshotSection>
            {
                ["system"] = SnapshotSection.FromData(new { computerName = "TEST-PC" })
            }
        };
        return new StringContent(JsonSerializer.Serialize(snapshot), Encoding.UTF8, "application/json");
    }

    [Fact]
    public async Task Ready_ReturnsReady_WithoutApiKey()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ready\"", body);
    }

    [Fact]
    public async Task Metrics_ReturnsCounters_WithoutApiKey()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("snapshotsReceived", body);
        Assert.Contains("snapshotsAccepted", body);
    }

    [Fact]
    public async Task Metrics_IncrementAfterSnapshotPost()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        long receivedBefore = await ReadCounter(client, "snapshotsReceived");
        long acceptedBefore = await ReadCounter(client, "snapshotsAccepted");

        using var content = BuildSnapshotContent("metrics-machine-001");
        HttpResponseMessage post = await client.PostAsync("/api/v1/snapshots", content);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);

        long receivedAfter = await ReadCounter(client, "snapshotsReceived");
        long acceptedAfter = await ReadCounter(client, "snapshotsAccepted");

        Assert.True(receivedAfter > receivedBefore, "snapshotsReceived should increase");
        Assert.True(acceptedAfter > acceptedBefore, "snapshotsAccepted should increase");
    }

    private static async Task<long> ReadCounter(HttpClient client, string name)
    {
        using JsonDocument doc = JsonDocument.Parse(await client.GetStringAsync("/metrics"));
        return doc.RootElement.GetProperty(name).GetInt64();
    }

    [Fact]
    public async Task Post_OversizedPayload_Returns413()
    {
        // Tighten the limit for this test so we do not have to send megabytes.
        using WebApplicationFactory<Program> factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Security:MaxRequestBodyBytes", "512"));

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        string big = new string('x', 2000);
        using var content = new StringContent(
            $"{{\"padding\":\"{big}\"}}", Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync("/api/v1/snapshots", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExceedingRateLimit_Returns429()
    {
        // Isolated host with a tiny fixed window so we can trip the limiter quickly.
        using WebApplicationFactory<Program> factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Security:RateLimit:PermitPerWindow", "3");
            builder.UseSetting("Security:RateLimit:WindowSeconds", "60");
        });

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 6; i++)
        {
            using var content = BuildSnapshotContent($"ratelimit-machine-{i}");
            HttpResponseMessage response = await client.PostAsync("/api/v1/snapshots", content);
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Accepted));
    }
}
