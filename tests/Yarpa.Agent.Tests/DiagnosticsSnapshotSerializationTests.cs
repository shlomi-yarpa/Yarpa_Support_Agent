using System.Text.Json;
using Yarpa.Agent.Collectors;
using Yarpa.Contracts;

namespace Yarpa.Agent.Tests;

public class DiagnosticsSnapshotSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Snapshot_Serializes_Metadata_As_CamelCase()
    {
        var snapshot = new DiagnosticsSnapshot
        {
            SnapshotId = Guid.Parse("5f3d1e2a-8c4b-4a9d-9f1e-2b7c6d5a4e3f"),
            AgentVersion = "1.0.0",
            MachineId = "a1b2c3d4",
            CollectedAtUtc = new DateTimeOffset(2026, 7, 6, 14, 3, 22, TimeSpan.Zero)
        };

        string json = JsonSerializer.Serialize(snapshot, SerializerOptions);

        Assert.Contains("\"snapshotId\":", json);
        Assert.Contains("\"schemaVersion\":\"1.0\"", json);
        Assert.Contains("\"agentVersion\":\"1.0.0\"", json);
        Assert.Contains("\"machineId\":\"a1b2c3d4\"", json);
        Assert.Contains("\"collectedAtUtc\":", json);
        Assert.Contains("\"sections\":", json);
    }

    [Fact]
    public void Section_Status_Serializes_As_Lowercase()
    {
        var okSection = SnapshotSection.FromData(new { computerName = "POS-01" });
        var errorSection = SnapshotSection.FromError("access denied");

        string okJson = JsonSerializer.Serialize(okSection, SerializerOptions);
        string errorJson = JsonSerializer.Serialize(errorSection, SerializerOptions);

        Assert.Contains("\"status\":\"ok\"", okJson);
        Assert.Contains("\"status\":\"error\"", errorJson);
        Assert.Contains("\"error\":\"access denied\"", errorJson);
    }

    [Fact]
    public void Ok_Section_Omits_Null_Error()
    {
        var okSection = SnapshotSection.FromData(new { drive = "C:" });

        string json = JsonSerializer.Serialize(okSection, SerializerOptions);

        Assert.DoesNotContain("\"error\":", json);
    }

    [Fact]
    public void CollectorResult_Maps_To_Section_Envelope()
    {
        CollectorResult result = CollectorResult.Ok("os", new { caption = "Windows 11 Pro" }, durationMs: 12);

        SnapshotSection section = result.ToSection();

        Assert.Equal(CollectorStatus.Ok, section.Status);
        Assert.NotNull(section.Data);
        Assert.Null(section.Error);
    }
}
