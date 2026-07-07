using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;
using Yarpa.Api.Services.Alerts;
using Yarpa.Api.Services.Alerts.Rules;
using Yarpa.Contracts;

namespace Yarpa.Api.Tests;

/// <summary>
/// Tests the <see cref="AlertEngine"/> reconciliation logic against an in-memory database:
/// raising, deduplicating open alerts of the same type, and auto-resolving cleared conditions.
/// </summary>
public class AlertEngineTests
{
    private static readonly JsonSerializerOptions SerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions DeserOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static YarpaDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<YarpaDbContext>()
            .UseInMemoryDatabase($"AlertEngine_{Guid.NewGuid()}")
            .Options;
        return new YarpaDbContext(options);
    }

    private static DiagnosticsSnapshot Build(
        string machineId,
        params (string name, string status, object? data)[] sections)
    {
        var raw = new
        {
            snapshotId = Guid.NewGuid(),
            schemaVersion = "1.0",
            agentVersion = "1.0.0",
            machineId,
            collectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sections = sections.ToDictionary(
                s => s.name,
                s => (object?)new { status = s.status, data = s.data })
        };
        string json = JsonSerializer.Serialize(raw, SerOptions);
        return JsonSerializer.Deserialize<DiagnosticsSnapshot>(json, DeserOptions)!;
    }

    private static AlertEngine CreateEngine(YarpaDbContext db, AlertOptions? options = null)
    {
        IAlertRule[] rules =
        {
            new ServiceDownRule(),
            new SqlNotRunningRule(),
            new DiskAlmostFullRule(),
            new PaymentTerminalMissingRule(),
            new OldSoftwareVersionRule(),
            new CollectorErrorRule()
        };
        return new AlertEngine(
            db,
            rules,
            Options.Create(options ?? new AlertOptions()),
            NullLogger<AlertEngine>.Instance);
    }

    private static MachineEntity SeedMachine(YarpaDbContext db, string machineId)
    {
        var machine = new MachineEntity
        {
            MachineId = machineId,
            CustomerId = Guid.NewGuid(),
            ComputerName = "PC",
            FirstSeenUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow
        };
        db.Machines.Add(machine);
        db.SaveChanges();
        return machine;
    }

    private static DiagnosticsSnapshot SqlSnapshot(string machineId, string serviceState) =>
        Build(machineId, ("sqlServer", "ok", new
        {
            installed = true,
            instances = new[] { new { name = "MSSQLSERVER", version = "15.0.2000", serviceState } }
        }));

    [Fact]
    public async Task ConditionHolds_CreatesAlert()
    {
        using YarpaDbContext db = NewDb();
        MachineEntity machine = SeedMachine(db, "m-1");
        AlertEngine engine = CreateEngine(db);

        DiagnosticsSnapshot snap = SqlSnapshot("m-1", "Stopped");
        db.Snapshots.Add(NewSnapshotEntity(snap));

        AlertEngineResult result = await engine.EvaluateAsync(snap, machine, Array.Empty<ChangeEntity>(), default);
        await db.SaveChangesAsync();

        Assert.True(result.RaisedCount >= 1);
        List<AlertEntity> open = await db.Alerts.Where(a => a.State == AlertState.Open).ToListAsync();
        Assert.Contains(open, a => a.AlertType == AlertType.SqlNotRunning);
    }

    [Fact]
    public async Task ConditionHoldsTwice_DoesNotDuplicateOpenAlert()
    {
        using YarpaDbContext db = NewDb();
        MachineEntity machine = SeedMachine(db, "m-2");
        AlertEngine engine = CreateEngine(db);

        DiagnosticsSnapshot snap1 = SqlSnapshot("m-2", "Stopped");
        db.Snapshots.Add(NewSnapshotEntity(snap1));
        await engine.EvaluateAsync(snap1, machine, Array.Empty<ChangeEntity>(), default);
        await db.SaveChangesAsync();

        DiagnosticsSnapshot snap2 = SqlSnapshot("m-2", "Stopped");
        db.Snapshots.Add(NewSnapshotEntity(snap2));
        await engine.EvaluateAsync(snap2, machine, Array.Empty<ChangeEntity>(), default);
        await db.SaveChangesAsync();

        int openSqlAlerts = await db.Alerts
            .CountAsync(a => a.AlertType == AlertType.SqlNotRunning && a.State == AlertState.Open);
        Assert.Equal(1, openSqlAlerts);
    }

    [Fact]
    public async Task ConditionClears_ResolvesOpenAlert()
    {
        using YarpaDbContext db = NewDb();
        MachineEntity machine = SeedMachine(db, "m-3");
        AlertEngine engine = CreateEngine(db);

        // First: SQL stopped → raises
        DiagnosticsSnapshot snap1 = SqlSnapshot("m-3", "Stopped");
        db.Snapshots.Add(NewSnapshotEntity(snap1));
        await engine.EvaluateAsync(snap1, machine, Array.Empty<ChangeEntity>(), default);
        await db.SaveChangesAsync();

        // Second: SQL running → resolves
        DiagnosticsSnapshot snap2 = SqlSnapshot("m-3", "Running");
        db.Snapshots.Add(NewSnapshotEntity(snap2));
        AlertEngineResult result = await engine.EvaluateAsync(snap2, machine, Array.Empty<ChangeEntity>(), default);
        await db.SaveChangesAsync();

        Assert.True(result.ResolvedCount >= 1);
        AlertEntity? alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == AlertType.SqlNotRunning);
        Assert.NotNull(alert);
        Assert.Equal(AlertState.Resolved, alert!.State);
        Assert.NotNull(alert.ResolvedAtUtc);
    }

    [Fact]
    public async Task SnapshotReceipt_ResolvesOpenNoRecentContact()
    {
        using YarpaDbContext db = NewDb();
        MachineEntity machine = SeedMachine(db, "m-4");
        db.Alerts.Add(new AlertEntity
        {
            MachineId = "m-4",
            AlertType = AlertType.NoRecentContact,
            Severity = AlertSeverity.Warning,
            Message = "no contact",
            State = AlertState.Open,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        });
        await db.SaveChangesAsync();

        AlertEngine engine = CreateEngine(db);
        DiagnosticsSnapshot snap = SqlSnapshot("m-4", "Running");
        db.Snapshots.Add(NewSnapshotEntity(snap));
        await engine.EvaluateAsync(snap, machine, Array.Empty<ChangeEntity>(), default);
        await db.SaveChangesAsync();

        AlertEntity alert = await db.Alerts.SingleAsync(a => a.AlertType == AlertType.NoRecentContact);
        Assert.Equal(AlertState.Resolved, alert.State);
    }

    [Fact]
    public async Task ErroredSection_DoesNotRaiseSqlNotRunning()
    {
        using YarpaDbContext db = NewDb();
        MachineEntity machine = SeedMachine(db, "m-5");
        AlertEngine engine = CreateEngine(db);

        DiagnosticsSnapshot snap = Build("m-5", ("sqlServer", "error", null));
        db.Snapshots.Add(NewSnapshotEntity(snap));
        await engine.EvaluateAsync(snap, machine, Array.Empty<ChangeEntity>(), default);
        await db.SaveChangesAsync();

        Assert.False(await db.Alerts.AnyAsync(a => a.AlertType == AlertType.SqlNotRunning));
        // But CollectorError should fire for the critical errored section.
        Assert.True(await db.Alerts.AnyAsync(a => a.AlertType == AlertType.CollectorError && a.State == AlertState.Open));
    }

    private static SnapshotEntity NewSnapshotEntity(DiagnosticsSnapshot snap) => new()
    {
        SnapshotId = snap.SnapshotId,
        MachineId = snap.MachineId,
        CollectedAtUtc = snap.CollectedAtUtc.UtcDateTime,
        ReceivedAtUtc = DateTime.UtcNow,
        AgentVersion = snap.AgentVersion,
        SchemaVersion = snap.SchemaVersion,
        RawJson = JsonSerializer.Serialize(snap, SerOptions)
    };
}
