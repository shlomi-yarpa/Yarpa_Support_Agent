using System.Net;
using System.Text;
using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Api.Tests.Infrastructure;

namespace Yarpa.Api.Tests;

/// <summary>
/// End-to-end integration tests for the alert engine and the alerts read endpoint.
/// Exercises the full POST /api/v1/snapshots ⇒ engine ⇒ GET /alerts flow through the API.
/// </summary>
[Collection("API Integration Tests")]
public class AlertsEndpointTests
{
    private readonly TestApiFactory _factory;

    public AlertsEndpointTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    private static StringContent SnapshotContent(
        string machineId,
        Guid? snapshotId = null,
        string sqlServiceState = "Running",
        double diskFreePercent = 45.0,
        double diskFreeGb = 200.0,
        string yarpaVersion = "8.4.2")
    {
        var snapshot = new
        {
            snapshotId = (snapshotId ?? Guid.NewGuid()).ToString(),
            schemaVersion = "1.0",
            agentVersion = "1.0.0",
            machineId,
            collectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sections = new
            {
                system = new { status = "ok", data = new { computerName = "TEST-PC" } },
                os = new
                {
                    status = "ok",
                    data = new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }
                },
                disks = new
                {
                    status = "ok",
                    data = new[]
                    {
                        new { drive = "C:", sizeGb = 476.9, freeGb = diskFreeGb, freePercent = diskFreePercent }
                    }
                },
                services = new
                {
                    status = "ok",
                    data = new[]
                    {
                        new { name = "MSSQLSERVER", displayName = "SQL Server", state = sqlServiceState, startMode = "Auto" }
                    }
                },
                sqlServer = new
                {
                    status = "ok",
                    data = new
                    {
                        installed = true,
                        instances = new[]
                        {
                            new { name = "MSSQLSERVER", version = "15.0.2000", serviceState = sqlServiceState }
                        }
                    }
                },
                yarpaVersion = new
                {
                    status = "ok",
                    data = new { product = "Yarpa ERP", version = yarpaVersion, detectedBy = "registry" }
                }
            }
        };

        string json = JsonSerializer.Serialize(snapshot);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private System.Net.Http.HttpClient AuthedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);
        return client;
    }

    [Fact]
    public async Task SqlStopped_RaisesAlerts_ThenRunning_ResolvesThem()
    {
        string machineId = $"alert-e2e-{Guid.NewGuid():N}";
        var client = AuthedClient();

        // First snapshot: SQL stopped → SqlNotRunning + ServiceDown
        using var down = SnapshotContent(machineId, sqlServiceState: "Stopped");
        var r1 = await client.PostAsync("/api/v1/snapshots", down);
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);

        var doc1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement;
        Assert.True(doc1.GetProperty("alerts").GetInt32() >= 2);

        // Open alerts must contain SqlNotRunning
        var open1 = await client.GetAsync($"/api/v1/machines/{machineId}/alerts?state=open");
        Assert.Equal(HttpStatusCode.OK, open1.StatusCode);
        var openDoc1 = JsonDocument.Parse(await open1.Content.ReadAsStringAsync()).RootElement;
        var types1 = openDoc1.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("alertType").GetString())
            .ToList();
        Assert.Contains(AlertType.SqlNotRunning, types1);
        Assert.Contains(AlertType.ServiceDown, types1);

        // Second snapshot: SQL running → both resolved
        using var up = SnapshotContent(machineId, sqlServiceState: "Running");
        var r2 = await client.PostAsync("/api/v1/snapshots", up);
        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);

        var open2 = await client.GetAsync($"/api/v1/machines/{machineId}/alerts?state=open");
        var openDoc2 = JsonDocument.Parse(await open2.Content.ReadAsStringAsync()).RootElement;
        var types2 = openDoc2.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("alertType").GetString())
            .ToList();
        Assert.DoesNotContain(AlertType.SqlNotRunning, types2);
        Assert.DoesNotContain(AlertType.ServiceDown, types2);

        // The resolved alerts still exist in history
        var resolved = await client.GetAsync($"/api/v1/machines/{machineId}/alerts?state=resolved");
        var resolvedDoc = JsonDocument.Parse(await resolved.Content.ReadAsStringAsync()).RootElement;
        Assert.True(resolvedDoc.GetProperty("totalCount").GetInt32() >= 2);
    }

    [Fact]
    public async Task DiskBelowThreshold_RaisesDiskAlmostFull()
    {
        string machineId = $"alert-disk-{Guid.NewGuid():N}";
        var client = AuthedClient();

        using var content = SnapshotContent(machineId, diskFreePercent: 3.0, diskFreeGb: 2.0);
        var r = await client.PostAsync("/api/v1/snapshots", content);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);

        var open = await client.GetAsync($"/api/v1/machines/{machineId}/alerts?state=open");
        var doc = JsonDocument.Parse(await open.Content.ReadAsStringAsync()).RootElement;
        var types = doc.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("alertType").GetString())
            .ToList();
        Assert.Contains(AlertType.DiskAlmostFull, types);
    }

    [Fact]
    public async Task NoDuplicateOpenAlert_AcrossRepeatedFailingSnapshots()
    {
        string machineId = $"alert-dup-{Guid.NewGuid():N}";
        var client = AuthedClient();

        for (int i = 0; i < 3; i++)
        {
            using var content = SnapshotContent(machineId, sqlServiceState: "Stopped");
            await client.PostAsync("/api/v1/snapshots", content);
        }

        var open = await client.GetAsync($"/api/v1/machines/{machineId}/alerts?state=open");
        var doc = JsonDocument.Parse(await open.Content.ReadAsStringAsync()).RootElement;
        int sqlAlerts = doc.GetProperty("items").EnumerateArray()
            .Count(i => i.GetProperty("alertType").GetString() == AlertType.SqlNotRunning);
        Assert.Equal(1, sqlAlerts);
    }

    [Fact]
    public async Task Alerts_OrderedBySeverityThenTime()
    {
        string machineId = $"alert-order-{Guid.NewGuid():N}";
        var client = AuthedClient();

        // SQL stopped (critical) + disk low (warning) in one snapshot
        using var content = SnapshotContent(machineId, sqlServiceState: "Stopped", diskFreePercent: 3.0, diskFreeGb: 2.0);
        await client.PostAsync("/api/v1/snapshots", content);

        var open = await client.GetAsync($"/api/v1/machines/{machineId}/alerts?state=open");
        var doc = JsonDocument.Parse(await open.Content.ReadAsStringAsync()).RootElement;
        var severities = doc.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("severity").GetString()!)
            .ToList();

        // Verify non-decreasing severity rank (critical first).
        var ranks = severities.Select(AlertSeverity.Rank).ToList();
        for (int i = 1; i < ranks.Count; i++)
            Assert.True(ranks[i] >= ranks[i - 1], "alerts must be ordered by severity (critical first)");
    }

    [Fact]
    public async Task GetAlerts_UnknownMachine_Returns404()
    {
        var client = AuthedClient();
        var response = await client.GetAsync("/api/v1/machines/no-such-machine/alerts");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoRecentContactScan_Endpoint_Returns200()
    {
        var client = AuthedClient();
        var response = await client.PostAsync("/api/v1/internal/alerts/scan-no-recent-contact", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(doc.TryGetProperty("scannedMachines", out _));
    }
}
