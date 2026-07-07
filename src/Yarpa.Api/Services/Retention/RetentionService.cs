using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Services.Retention;

/// <summary>
/// Deletes raw snapshots older than the configured retention window, subject to strict
/// safety rules: the newest snapshot per machine, snapshots referenced by a Change or an
/// Alert, and the most recent N snapshots per machine are always retained. Changes and
/// Alerts are never touched, so the diagnostic history remains intact.
/// </summary>
public sealed class RetentionService : IRetentionService
{
    private readonly YarpaDbContext _db;
    private readonly RetentionOptions _options;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        YarpaDbContext db,
        IOptions<RetentionOptions> options,
        ILogger<RetentionService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RetentionResult> RunAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("retention disabled; skipping run");
            return new RetentionResult { Enabled = false, CutoffUtc = DateTime.UtcNow };
        }

        int retainDays = Math.Max(1, _options.RetainDays);
        int minPerMachine = Math.Max(0, _options.MinSnapshotsPerMachine);
        int maxDelete = Math.Max(1, _options.MaxDeletePerRun);
        DateTime cutoff = DateTime.UtcNow.AddDays(-retainDays);

        // ── Snapshots older than the cutoff (only the ids; keeps the working set small) ──
        List<Guid> oldIds = await _db.Snapshots
            .Where(s => s.CollectedAtUtc < cutoff)
            .Select(s => s.SnapshotId)
            .ToListAsync(ct);

        if (oldIds.Count == 0)
        {
            _logger.LogInformation("retention: no snapshots older than {Cutoff:o}", cutoff);
            return new RetentionResult { Enabled = true, CutoffUtc = cutoff };
        }

        // ── Build the protected set (never deleted) ──────────────────────────────
        var protectedIds = new HashSet<Guid>();

        protectedIds.UnionWith(await _db.Machines
            .Where(m => m.LastSnapshotId != null)
            .Select(m => m.LastSnapshotId!.Value)
            .ToListAsync(ct));

        protectedIds.UnionWith(await _db.Changes
            .Select(c => c.SnapshotId)
            .Distinct()
            .ToListAsync(ct));

        protectedIds.UnionWith(await _db.Alerts
            .Where(a => a.SourceSnapshotId != null)
            .Select(a => a.SourceSnapshotId!.Value)
            .Distinct()
            .ToListAsync(ct));

        // Keep the newest N snapshots per machine regardless of age.
        if (minPerMachine > 0)
        {
            List<string> machineIds = await _db.Snapshots
                .Select(s => s.MachineId)
                .Distinct()
                .ToListAsync(ct);

            foreach (string machineId in machineIds)
            {
                List<Guid> keep = await _db.Snapshots
                    .Where(s => s.MachineId == machineId)
                    .OrderByDescending(s => s.CollectedAtUtc)
                    .Select(s => s.SnapshotId)
                    .Take(minPerMachine)
                    .ToListAsync(ct);

                protectedIds.UnionWith(keep);
            }
        }

        // ── Determine deletable set (in memory; capped per run) ──────────────────
        List<Guid> deletable = oldIds
            .Where(id => !protectedIds.Contains(id))
            .Take(maxDelete)
            .ToList();

        int protectedOld = oldIds.Count(id => protectedIds.Contains(id));

        if (deletable.Count == 0)
        {
            _logger.LogInformation(
                "retention: {Old} snapshot(s) older than cutoff, all protected; nothing deleted",
                oldIds.Count);
            return new RetentionResult
            {
                Enabled = true,
                CutoffUtc = cutoff,
                OlderThanCutoff = oldIds.Count,
                Protected = protectedOld,
                Deleted = 0
            };
        }

        List<SnapshotEntity> entities = await _db.Snapshots
            .Where(s => deletable.Contains(s.SnapshotId))
            .ToListAsync(ct);

        _db.Snapshots.RemoveRange(entities);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "retention: deleted {Deleted} snapshot(s) older than {Cutoff:o} (candidates={Old}, protected={Protected})",
            entities.Count, cutoff, oldIds.Count, protectedOld);

        return new RetentionResult
        {
            Enabled = true,
            CutoffUtc = cutoff,
            OlderThanCutoff = oldIds.Count,
            Protected = protectedOld,
            Deleted = entities.Count
        };
    }
}
