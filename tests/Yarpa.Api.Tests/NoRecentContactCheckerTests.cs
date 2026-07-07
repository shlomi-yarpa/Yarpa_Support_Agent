using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;
using Yarpa.Api.Services.Alerts;

namespace Yarpa.Api.Tests;

/// <summary>
/// Tests the time-based <see cref="NoRecentContactChecker"/>: raising for stale machines,
/// no duplicates, and resolving once a machine is back in contact.
/// </summary>
public class NoRecentContactCheckerTests
{
    private static YarpaDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<YarpaDbContext>()
            .UseInMemoryDatabase($"NoContact_{Guid.NewGuid()}")
            .Options;
        return new YarpaDbContext(options);
    }

    private static NoRecentContactChecker CreateChecker(YarpaDbContext db, int days = 3)
    {
        var options = Options.Create(new AlertOptions { NoRecentContactDays = days });
        return new NoRecentContactChecker(db, options, NullLogger<NoRecentContactChecker>.Instance);
    }

    private static void SeedMachine(YarpaDbContext db, string machineId, DateTime lastSeenUtc)
    {
        db.Machines.Add(new MachineEntity
        {
            MachineId = machineId,
            CustomerId = Guid.NewGuid(),
            ComputerName = "PC",
            FirstSeenUtc = lastSeenUtc,
            LastSeenUtc = lastSeenUtc
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task StaleMachine_RaisesNoRecentContact()
    {
        using YarpaDbContext db = NewDb();
        SeedMachine(db, "stale", DateTime.UtcNow.AddDays(-10));

        NoRecentContactResult result = await CreateChecker(db).RunAsync(default);

        Assert.Equal(1, result.RaisedCount);
        Assert.True(await db.Alerts.AnyAsync(a =>
            a.MachineId == "stale" && a.AlertType == AlertType.NoRecentContact && a.State == AlertState.Open));
    }

    [Fact]
    public async Task RecentMachine_DoesNotRaise()
    {
        using YarpaDbContext db = NewDb();
        SeedMachine(db, "fresh", DateTime.UtcNow.AddHours(-1));

        NoRecentContactResult result = await CreateChecker(db).RunAsync(default);

        Assert.Equal(0, result.RaisedCount);
        Assert.False(await db.Alerts.AnyAsync(a => a.AlertType == AlertType.NoRecentContact));
    }

    [Fact]
    public async Task RunTwice_DoesNotDuplicate()
    {
        using YarpaDbContext db = NewDb();
        SeedMachine(db, "stale", DateTime.UtcNow.AddDays(-10));

        await CreateChecker(db).RunAsync(default);
        await CreateChecker(db).RunAsync(default);

        int open = await db.Alerts.CountAsync(a =>
            a.AlertType == AlertType.NoRecentContact && a.State == AlertState.Open);
        Assert.Equal(1, open);
    }

    [Fact]
    public async Task BackInContact_ResolvesAlert()
    {
        using YarpaDbContext db = NewDb();
        SeedMachine(db, "m", DateTime.UtcNow.AddDays(-10));

        await CreateChecker(db).RunAsync(default);

        // Machine reports in: update LastSeenUtc to now, run again.
        MachineEntity machine = await db.Machines.SingleAsync(m => m.MachineId == "m");
        machine.LastSeenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        NoRecentContactResult result = await CreateChecker(db).RunAsync(default);

        Assert.Equal(1, result.ResolvedCount);
        AlertEntity alert = await db.Alerts.SingleAsync(a => a.AlertType == AlertType.NoRecentContact);
        Assert.Equal(AlertState.Resolved, alert.State);
    }
}
