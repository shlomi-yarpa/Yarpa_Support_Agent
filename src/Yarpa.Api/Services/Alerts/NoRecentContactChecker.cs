using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Services.Alerts;

/// <summary>
/// Scans all machines and reconciles <see cref="AlertType.NoRecentContact"/> alerts:
/// raises one for a machine whose LastSeenUtc is older than the configured threshold and has no
/// open NoRecentContact alert, and resolves the alert once a machine is back in contact.
/// This checker persists its own changes (it runs outside the snapshot ingest transaction).
/// </summary>
public sealed class NoRecentContactChecker : INoRecentContactChecker
{
    private readonly YarpaDbContext _db;
    private readonly AlertOptions _options;
    private readonly ILogger<NoRecentContactChecker> _logger;

    public NoRecentContactChecker(
        YarpaDbContext db,
        IOptions<AlertOptions> options,
        ILogger<NoRecentContactChecker> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NoRecentContactResult> RunAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        DateTime threshold = now.AddDays(-_options.NoRecentContactDays);

        List<MachineEntity> machines = await _db.Machines.ToListAsync(ct);

        List<AlertEntity> openContactAlerts = await _db.Alerts
            .Where(a => a.AlertType == AlertType.NoRecentContact && a.State == AlertState.Open)
            .ToListAsync(ct);

        Dictionary<string, List<AlertEntity>> openByMachine = openContactAlerts
            .GroupBy(a => a.MachineId)
            .ToDictionary(g => g.Key, g => g.ToList());

        int raised = 0;
        int resolved = 0;

        foreach (MachineEntity machine in machines)
        {
            bool stale = machine.LastSeenUtc < threshold;
            openByMachine.TryGetValue(machine.MachineId, out List<AlertEntity>? existing);
            bool hasOpen = existing is { Count: > 0 };

            if (stale && !hasOpen)
            {
                int days = (int)Math.Floor((now - machine.LastSeenUtc).TotalDays);
                string lastSeen = machine.LastSeenUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
                _db.Alerts.Add(new AlertEntity
                {
                    MachineId = machine.MachineId,
                    AlertType = AlertType.NoRecentContact,
                    Severity = AlertSeverity.Warning,
                    Message = $"לא התקבל דיווח מהמחשב מזה {days} ימים (נראה לאחרונה: {lastSeen}).",
                    State = AlertState.Open,
                    CreatedAtUtc = now,
                    SourceSnapshotId = null,
                    SourceChangeId = null
                });
                raised++;
            }
            else if (!stale && hasOpen)
            {
                foreach (AlertEntity a in existing!)
                {
                    a.State = AlertState.Resolved;
                    a.ResolvedAtUtc = now;
                    resolved++;
                }
            }
        }

        if (raised > 0 || resolved > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "no-recent-contact scan: machines={Scanned} raised={Raised} resolved={Resolved} (threshold={Days}d)",
            machines.Count, raised, resolved, _options.NoRecentContactDays);

        return new NoRecentContactResult
        {
            RaisedCount = raised,
            ResolvedCount = resolved,
            ScannedMachines = machines.Count
        };
    }
}
