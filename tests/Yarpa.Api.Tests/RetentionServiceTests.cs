using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;
using Yarpa.Api.Services.Retention;

namespace Yarpa.Api.Tests;

/// <summary>
/// Tests the snapshot retention job: it prunes old raw snapshots only when enabled and
/// always preserves the newest snapshot per machine, the newest N, snapshots referenced by
/// a Change or Alert, and every historical Change/Alert.
/// </summary>
public class RetentionServiceTests
{
    private static YarpaDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<YarpaDbContext>()
            .UseInMemoryDatabase($"Retention_{Guid.NewGuid()}")
            .Options;
        return new YarpaDbContext(options);
    }

    private static RetentionService CreateService(YarpaDbContext db, RetentionOptions options) =>
        new(db, Options.Create(options), NullLogger<RetentionService>.Instance);

    private static Guid SeedSnapshot(YarpaDbContext db, string machineId, DateTime collectedAtUtc)
    {
        var id = Guid.NewGuid();
        db.Snapshots.Add(new SnapshotEntity
        {
            SnapshotId = id,
            MachineId = machineId,
            CollectedAtUtc = collectedAtUtc,
            ReceivedAtUtc = collectedAtUtc,
            AgentVersion = "1.0.0",
            SchemaVersion = "1.0",
            RawJson = "{}"
        });
        return id;
    }

    private static void SeedMachine(YarpaDbContext db, string machineId, Guid lastSnapshotId)
    {
        db.Machines.Add(new MachineEntity
        {
            MachineId = machineId,
            CustomerId = Guid.NewGuid(),
            ComputerName = "PC",
            FirstSeenUtc = DateTime.UtcNow.AddDays(-200),
            LastSeenUtc = DateTime.UtcNow,
            LastSnapshotId = lastSnapshotId
        });
    }

    [Fact]
    public async Task Disabled_DeletesNothing()
    {
        using YarpaDbContext db = NewDb();
        Guid last = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-1));
        SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-300));
        SeedMachine(db, "m1", last);
        await db.SaveChangesAsync();

        RetentionResult result = await CreateService(db,
            new RetentionOptions { Enabled = false, RetainDays = 30 }).RunAsync(default);

        Assert.False(result.Enabled);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(2, await db.Snapshots.CountAsync());
    }

    [Fact]
    public async Task DeletesOldUnreferencedSnapshots_KeepsNewestNAndLast()
    {
        using YarpaDbContext db = NewDb();
        SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-100));
        SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-90));
        SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-80));
        Guid old70 = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-70));
        Guid recent = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-5));
        SeedMachine(db, "m1", recent);
        await db.SaveChangesAsync();

        RetentionResult result = await CreateService(db,
            new RetentionOptions { Enabled = true, RetainDays = 30, MinSnapshotsPerMachine = 2 })
            .RunAsync(default);

        // -100/-90/-80 deleted; -70 kept (newest-2), -5 kept (newest + last).
        Assert.Equal(3, result.Deleted);
        Assert.Equal(2, await db.Snapshots.CountAsync());
        Assert.True(await db.Snapshots.AnyAsync(s => s.SnapshotId == old70));
        Assert.True(await db.Snapshots.AnyAsync(s => s.SnapshotId == recent));
    }

    [Fact]
    public async Task PreservesReferencedSnapshotsAndKeepsChanges()
    {
        using YarpaDbContext db = NewDb();
        Guid unreferenced = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-110));
        Guid referenced = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-100));
        Guid last = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-90));
        SeedMachine(db, "m1", last);
        db.Changes.Add(new ChangeEntity
        {
            MachineId = "m1",
            SnapshotId = referenced,
            ChangeType = "OsChanged",
            SectionName = "os",
            DetectedAtUtc = DateTime.UtcNow.AddDays(-100)
        });
        await db.SaveChangesAsync();

        RetentionResult result = await CreateService(db,
            new RetentionOptions { Enabled = true, RetainDays = 30, MinSnapshotsPerMachine = 0 })
            .RunAsync(default);

        // Only the unreferenced, non-last snapshot is removed.
        Assert.Equal(1, result.Deleted);
        Assert.False(await db.Snapshots.AnyAsync(s => s.SnapshotId == unreferenced));
        Assert.True(await db.Snapshots.AnyAsync(s => s.SnapshotId == referenced));
        Assert.True(await db.Snapshots.AnyAsync(s => s.SnapshotId == last));
        // The historical change survives untouched.
        Assert.Equal(1, await db.Changes.CountAsync());
    }

    [Fact]
    public async Task PreservesSnapshotReferencedByAlert()
    {
        using YarpaDbContext db = NewDb();
        Guid alertSource = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-120));
        Guid last = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-90));
        SeedMachine(db, "m1", last);
        db.Alerts.Add(new AlertEntity
        {
            MachineId = "m1",
            AlertType = AlertType.DiskAlmostFull,
            Severity = AlertSeverity.Warning,
            Message = "דיסק כמעט מלא",
            State = AlertState.Open,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-120),
            SourceSnapshotId = alertSource
        });
        await db.SaveChangesAsync();

        RetentionResult result = await CreateService(db,
            new RetentionOptions { Enabled = true, RetainDays = 30, MinSnapshotsPerMachine = 0 })
            .RunAsync(default);

        Assert.Equal(0, result.Deleted);
        Assert.True(await db.Snapshots.AnyAsync(s => s.SnapshotId == alertSource));
        Assert.Equal(1, await db.Alerts.CountAsync());
    }

    [Fact]
    public async Task RespectsMaxDeletePerRun()
    {
        using YarpaDbContext db = NewDb();
        for (int i = 0; i < 10; i++)
            SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-200 + i));
        Guid last = SeedSnapshot(db, "m1", DateTime.UtcNow.AddDays(-1));
        SeedMachine(db, "m1", last);
        await db.SaveChangesAsync();

        RetentionResult result = await CreateService(db,
            new RetentionOptions
            {
                Enabled = true,
                RetainDays = 30,
                MinSnapshotsPerMachine = 0,
                MaxDeletePerRun = 4
            }).RunAsync(default);

        Assert.Equal(4, result.Deleted);
        Assert.Equal(7, await db.Snapshots.CountAsync()); // 11 - 4
    }
}
