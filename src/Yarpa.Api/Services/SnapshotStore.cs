using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services;

/// <summary>
/// Stores a snapshot append-only and extracts decoded columns for fast querying.
/// A snapshot that already exists (same SnapshotId) is silently ignored (idempotency).
/// After storing a new snapshot the comparer runs and detected changes are persisted
/// in the same database transaction.
/// </summary>
public sealed class SnapshotStore : ISnapshotStore
{
    private readonly YarpaDbContext _db;
    private readonly ISnapshotComparer _comparer;
    private readonly ILogger<SnapshotStore> _logger;

    public SnapshotStore(
        YarpaDbContext db,
        ISnapshotComparer comparer,
        ILogger<SnapshotStore> logger)
    {
        _db = db;
        _comparer = comparer;
        _logger = logger;
    }

    public async Task<SnapshotStoreResult> StoreAsync(
        DiagnosticsSnapshot snapshot,
        string rawJson,
        MachineEntity machine,
        CancellationToken ct)
    {
        // ── Idempotency check ────────────────────────────────────────────────
        bool exists = await _db.Snapshots
            .AnyAsync(s => s.SnapshotId == snapshot.SnapshotId, ct);

        if (exists)
        {
            _logger.LogInformation(
                "snapshot {SnapshotId} already stored; skipping (idempotent re-send)",
                snapshot.SnapshotId);

            // Return existing change count for this snapshot so callers get accurate data
            int existingChanges = await _db.Changes
                .CountAsync(c => c.SnapshotId == snapshot.SnapshotId, ct);

            return new SnapshotStoreResult
            {
                SnapshotId = snapshot.SnapshotId,
                MachineId = machine.MachineId,
                Changes = existingChanges,
                IsNew = false
            };
        }

        // ── Load previous snapshot raw JSON for comparison ───────────────────
        string? previousRawJson = null;
        if (machine.LastSnapshotId.HasValue)
        {
            previousRawJson = await _db.Snapshots
                .Where(s => s.SnapshotId == machine.LastSnapshotId.Value)
                .Select(s => s.RawJson)
                .FirstOrDefaultAsync(ct);
        }

        // ── Extract decoded columns ──────────────────────────────────────────
        var decoded = ExtractDecodedColumns(snapshot);

        var entity = new SnapshotEntity
        {
            SnapshotId = snapshot.SnapshotId,
            MachineId = machine.MachineId,
            CollectedAtUtc = snapshot.CollectedAtUtc.UtcDateTime,
            ReceivedAtUtc = DateTime.UtcNow,
            AgentVersion = snapshot.AgentVersion,
            SchemaVersion = snapshot.SchemaVersion,
            RawJson = rawJson,
            OsCaption = decoded.OsCaption,
            OsBuild = decoded.OsBuild,
            RamTotalMb = decoded.RamTotalMb,
            MinFreeDiskPercent = decoded.MinFreeDiskPercent,
            YarpaVersion = decoded.YarpaVersion,
            SqlInstalled = decoded.SqlInstalled
        };

        _db.Snapshots.Add(entity);

        // ── Run comparison ───────────────────────────────────────────────────
        IReadOnlyList<ChangeEntity> changes = _comparer.Compare(snapshot, previousRawJson);
        if (changes.Count > 0)
            _db.Changes.AddRange(changes);

        // ── Update machine metadata ──────────────────────────────────────────
        machine.LastSeenUtc = DateTime.UtcNow;
        machine.LastSnapshotId = snapshot.SnapshotId;

        if (snapshot.Sections.TryGetValue("system", out var sysSection)
            && sysSection.Data is JsonElement sysEl
            && sysEl.TryGetProperty("computerName", out var cnProp))
        {
            string? name = cnProp.GetString();
            if (!string.IsNullOrEmpty(name))
                machine.ComputerName = name;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "stored snapshot {SnapshotId} for machine {MachineId} with {Changes} change(s)",
            snapshot.SnapshotId, machine.MachineId, changes.Count);

        return new SnapshotStoreResult
        {
            SnapshotId = snapshot.SnapshotId,
            MachineId = machine.MachineId,
            Changes = changes.Count,
            IsNew = true
        };
    }

    private static DecodedColumns ExtractDecodedColumns(DiagnosticsSnapshot snapshot)
    {
        string? osCaption = null;
        string? osBuild = null;
        long? ramTotalMb = null;
        double? minFreeDiskPercent = null;
        string? yarpaVersion = null;
        bool? sqlInstalled = null;

        if (snapshot.Sections.TryGetValue("os", out var osSection)
            && osSection.Data is JsonElement osEl)
        {
            osCaption = osEl.TryGetProperty("caption", out var p) ? p.GetString() : null;
            osBuild = osEl.TryGetProperty("build", out var b) ? b.GetString() : null;
        }

        if (snapshot.Sections.TryGetValue("hardware", out var hwSection)
            && hwSection.Data is JsonElement hwEl
            && hwEl.TryGetProperty("ramTotalMb", out var ramProp)
            && ramProp.TryGetInt64(out long ram))
        {
            ramTotalMb = ram;
        }

        if (snapshot.Sections.TryGetValue("disks", out var diskSection)
            && diskSection.Data is JsonElement diskEl
            && diskEl.ValueKind == JsonValueKind.Array)
        {
            double min = double.MaxValue;
            foreach (JsonElement disk in diskEl.EnumerateArray())
            {
                if (disk.TryGetProperty("freePercent", out var fp) && fp.TryGetDouble(out double pct))
                    min = Math.Min(min, pct);
            }
            if (min < double.MaxValue)
                minFreeDiskPercent = Math.Round(min, 1);
        }

        if (snapshot.Sections.TryGetValue("yarpaVersion", out var yvSection)
            && yvSection.Data is JsonElement yvEl
            && yvEl.TryGetProperty("version", out var vvProp))
        {
            yarpaVersion = vvProp.GetString();
        }

        if (snapshot.Sections.TryGetValue("sqlServer", out var sqlSection)
            && sqlSection.Data is JsonElement sqlEl
            && sqlEl.TryGetProperty("installed", out var instProp))
        {
            sqlInstalled = instProp.GetBoolean();
        }

        return new DecodedColumns
        {
            OsCaption = osCaption,
            OsBuild = osBuild,
            RamTotalMb = ramTotalMb,
            MinFreeDiskPercent = minFreeDiskPercent,
            YarpaVersion = yarpaVersion,
            SqlInstalled = sqlInstalled
        };
    }

    private sealed record DecodedColumns(
        string? OsCaption = null,
        string? OsBuild = null,
        long? RamTotalMb = null,
        double? MinFreeDiskPercent = null,
        string? YarpaVersion = null,
        bool? SqlInstalled = null);
}
