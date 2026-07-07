using System.Net;
using System.Text;
using System.Text.Json;
using Yarpa.Api.Tests.Infrastructure;

namespace Yarpa.Api.Tests;

/// <summary>
/// Integration tests for the Stage-5 read endpoints:
/// GET /api/v1/machines, /summary, /snapshots, and GET /api/v1/snapshots/{id}.
/// </summary>
[Collection("API Integration Tests")]
public class MachinesEndpointTests
{
    private readonly TestApiFactory _factory;

    public MachinesEndpointTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private HttpClient AuthedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);
        return client;
    }

    private static StringContent FullSnapshotContent(string machineId)
    {
        var body = new
        {
            snapshotId = Guid.NewGuid().ToString(),
            schemaVersion = "1.0",
            agentVersion = "1.0.0",
            machineId,
            collectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sections = new
            {
                system = new
                {
                    status = "ok",
                    data = new { computerName = "TEST-PC-READ" }
                },
                os = new
                {
                    status = "ok",
                    data = new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631", edition = "Pro", architecture = "64-bit" }
                },
                hardware = new
                {
                    status = "ok",
                    data = new { manufacturer = "Dell", model = "OptiPlex 7090", ramTotalMb = 16384, ramModules = 2 }
                },
                disks = new
                {
                    status = "ok",
                    data = new[]
                    {
                        new { drive = "C:", sizeGb = 476.9, freeGb = 210.3, freePercent = 44.1, mediaType = "SSD" }
                    }
                },
                yarpaVersion = new
                {
                    status = "ok",
                    data = new { product = "Yarpa ERP", version = "8.4.2", detectedBy = "registry" }
                },
                sqlServer = new
                {
                    status = "ok",
                    data = new
                    {
                        installed = true,
                        instances = new[]
                        {
                            new { name = "MSSQLSERVER", version = "15.0.2000", serviceState = "Running" }
                        }
                    }
                },
                paymentTerminals = new
                {
                    status = "ok",
                    data = new[]
                    {
                        new { vendor = "Verifone", model = "VX520", comPort = "COM3", vid = "11CA", pid = "0300" }
                    }
                },
                printers = new
                {
                    status = "ok",
                    data = new[]
                    {
                        new { name = "EPSON TM-T20", isDefault = true, status = "Idle", portName = "USB001", driver = "EPSON TM-T20" }
                    }
                },
                usbDevices = new
                {
                    status = "ok",
                    data = new[]
                    {
                        new { name = "USB Serial Device", vid = "11CA", pid = "0300", deviceClass = "Ports", manufacturer = "Verifone" }
                    }
                }
            }
        };

        return new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");
    }

    // ── GET /api/v1/machines ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMachines_WithValidKey_ReturnsOk()
    {
        var client = AuthedClient();

        var response = await client.GetAsync("/api/v1/machines");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(doc.TryGetProperty("totalCount", out _));
        Assert.True(doc.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task GetMachines_WithoutApiKey_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/machines");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMachines_AfterSnapshot_IncludesMachine()
    {
        string machineId = $"read-machines-{Guid.NewGuid():N}";
        var client = AuthedClient();

        using var content = FullSnapshotContent(machineId);
        var post = await client.PostAsync("/api/v1/snapshots", content);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);

        var response = await client.GetAsync("/api/v1/machines");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var machineIds = doc.GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("machineId").GetString())
            .ToList();

        Assert.Contains(machineId, machineIds);
    }

    [Fact]
    public async Task GetMachines_SearchByName_FiltersResults()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string machineId = $"search-test-{suffix}";
        var client = AuthedClient();

        using var content = FullSnapshotContent(machineId);
        await client.PostAsync("/api/v1/snapshots", content);

        // Search by computer name that was set in the snapshot
        var response = await client.GetAsync("/api/v1/machines?search=TEST-PC-READ");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(doc.GetProperty("totalCount").GetInt32() >= 1);
    }

    // ── GET /api/v1/machines/{id}/summary ─────────────────────────────────────

    [Fact]
    public async Task GetSummary_AfterSnapshot_ReturnsSummaryFields()
    {
        string machineId = $"summary-{Guid.NewGuid():N}";
        var client = AuthedClient();

        using var content = FullSnapshotContent(machineId);
        await client.PostAsync("/api/v1/snapshots", content);

        var response = await client.GetAsync($"/api/v1/machines/{machineId}/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        // Core machine fields
        Assert.Equal(machineId, doc.GetProperty("machineId").GetString());
        Assert.Equal("TEST-PC-READ", doc.GetProperty("computerName").GetString());

        // OS section was parsed
        var os = doc.GetProperty("os");
        Assert.Equal("Windows 11 Pro", os.GetProperty("caption").GetString());
        Assert.Equal("22631", os.GetProperty("build").GetString());

        // Yarpa version parsed
        var yarpa = doc.GetProperty("yarpaVersion");
        Assert.Equal("8.4.2", yarpa.GetProperty("version").GetString());

        // Hardware parsed
        var hw = doc.GetProperty("hardware");
        Assert.Equal(16384, hw.GetProperty("ramTotalMb").GetInt64());

        // SQL parsed
        var sql = doc.GetProperty("sqlServer");
        Assert.True(sql.GetProperty("installed").GetBoolean());

        // Disks parsed
        var disks = doc.GetProperty("disks");
        Assert.Equal(JsonValueKind.Array, disks.ValueKind);
        Assert.True(disks.GetArrayLength() >= 1);

        // Payment terminals parsed
        var terminals = doc.GetProperty("paymentTerminals");
        Assert.Equal(JsonValueKind.Array, terminals.ValueKind);
        Assert.True(terminals.GetArrayLength() >= 1);
        Assert.Equal("Verifone", terminals[0].GetProperty("vendor").GetString());
    }

    [Fact]
    public async Task GetSummary_UnknownMachine_Returns404()
    {
        var client = AuthedClient();

        var response = await client.GetAsync("/api/v1/machines/no-such-machine-xyz/summary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /api/v1/machines/{id}/snapshots ───────────────────────────────────

    [Fact]
    public async Task GetSnapshots_AfterSnapshot_ReturnsPaged()
    {
        string machineId = $"snaps-page-{Guid.NewGuid():N}";
        var client = AuthedClient();

        // Post two distinct snapshots
        using var c1 = FullSnapshotContent(machineId);
        await client.PostAsync("/api/v1/snapshots", c1);
        using var c2 = FullSnapshotContent(machineId);
        await client.PostAsync("/api/v1/snapshots", c2);

        var response = await client.GetAsync($"/api/v1/machines/{machineId}/snapshots");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(machineId, doc.GetProperty("machineId").GetString());
        Assert.True(doc.GetProperty("totalCount").GetInt32() >= 2);

        // Items should not include rawJson
        var item = doc.GetProperty("items")[0];
        Assert.True(item.TryGetProperty("snapshotId", out _));
        Assert.True(item.TryGetProperty("collectedAtUtc", out _));
        Assert.False(item.TryGetProperty("rawJson", out _));
    }

    [Fact]
    public async Task GetSnapshots_UnknownMachine_Returns404()
    {
        var client = AuthedClient();

        var response = await client.GetAsync("/api/v1/machines/no-such-machine-xyz/snapshots");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /api/v1/snapshots/{snapshotId} ────────────────────────────────────

    [Fact]
    public async Task GetRawSnapshot_AfterPost_ReturnsFullJson()
    {
        string machineId = $"raw-snap-{Guid.NewGuid():N}";
        var client = AuthedClient();

        var snapshotGuid = Guid.NewGuid();
        var body = new
        {
            snapshotId = snapshotGuid.ToString(),
            schemaVersion = "1.0",
            agentVersion = "1.0.0",
            machineId,
            collectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sections = new
            {
                system = new { status = "ok", data = new { computerName = "RAW-PC" } }
            }
        };
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var post = await client.PostAsync("/api/v1/snapshots", content);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);

        var response = await client.GetAsync($"/api/v1/snapshots/{snapshotGuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(snapshotGuid.ToString(), doc.GetProperty("snapshotId").GetString());
        Assert.Equal(machineId, doc.GetProperty("machineId").GetString());
        Assert.True(doc.TryGetProperty("sections", out _));
    }

    [Fact]
    public async Task GetRawSnapshot_UnknownId_Returns404()
    {
        var client = AuthedClient();

        var response = await client.GetAsync($"/api/v1/snapshots/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
