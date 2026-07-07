using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Controllers;

/// <summary>
/// Read endpoints for machines: list, summary, snapshots, changes, alerts.
/// All endpoints require a valid X-Api-Key; the authenticated customer is resolved
/// by ApiKeyMiddleware and stored in HttpContext.Items["Customer"].
/// </summary>
[ApiController]
[Route("api/v1/machines")]
public sealed class MachinesController : ControllerBase
{
    private readonly YarpaDbContext _db;

    public MachinesController(YarpaDbContext db)
    {
        _db = db;
    }

    // ── GET /api/v1/machines ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a paged list of machines belonging to the authenticated customer.
    /// Optional <paramref name="search"/> filters by computer name or machine ID (case-insensitive contains).
    /// GET /api/v1/machines?search=&page=1&pageSize=50
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(MachinesPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMachines(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var customer = (CustomerEntity)HttpContext.Items["Customer"]!;

        IQueryable<MachineEntity> query = _db.Machines
            .Where(m => m.CustomerId == customer.CustomerId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(m =>
                m.ComputerName.Contains(term) ||
                m.MachineId.Contains(term));
        }

        int totalCount = await query.CountAsync(ct);

        List<MachineEntity> machines = await query
            .OrderBy(m => m.ComputerName)
            .ThenBy(m => m.MachineId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Fetch open-alert counts for all machines in one query
        var machineIds = machines.Select(m => m.MachineId).ToList();
        Dictionary<string, int> openAlertCounts = await _db.Alerts
            .Where(a => machineIds.Contains(a.MachineId) && a.State == AlertState.Open)
            .GroupBy(a => a.MachineId)
            .Select(g => new { MachineId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MachineId, x => x.Count, ct);

        return Ok(new MachinesPageDto
        {
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize,
            Items      = machines.ConvertAll(m => new MachineListItemDto
            {
                MachineId      = m.MachineId,
                ComputerName   = m.ComputerName,
                FirstSeenUtc   = m.FirstSeenUtc,
                LastSeenUtc    = m.LastSeenUtc,
                LastSnapshotId = m.LastSnapshotId,
                OpenAlertCount = openAlertCounts.GetValueOrDefault(m.MachineId, 0)
            })
        });
    }

    // ── GET /api/v1/machines/{machineId}/summary ───────────────────────────────

    /// <summary>
    /// Returns a decoded summary of the latest snapshot for the specified machine.
    /// GET /api/v1/machines/{machineId}/summary
    /// </summary>
    [HttpGet("{machineId}/summary")]
    [ProducesResponseType(typeof(MachineSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(
        string machineId,
        CancellationToken ct = default)
    {
        var customer = (CustomerEntity)HttpContext.Items["Customer"]!;

        MachineEntity? machine = await _db.Machines
            .FirstOrDefaultAsync(m => m.MachineId == machineId && m.CustomerId == customer.CustomerId, ct);

        if (machine == null)
            return NotFound(new { error = $"machine '{machineId}' not found" });

        if (!machine.LastSnapshotId.HasValue)
        {
            // Machine registered but no snapshot yet
            return Ok(new MachineSummaryDto
            {
                MachineId    = machine.MachineId,
                ComputerName = machine.ComputerName,
                FirstSeenUtc = machine.FirstSeenUtc,
                LastSeenUtc  = machine.LastSeenUtc
            });
        }

        SnapshotEntity? snapshot = await _db.Snapshots
            .FirstOrDefaultAsync(s => s.SnapshotId == machine.LastSnapshotId.Value, ct);

        if (snapshot == null)
            return NotFound(new { error = $"snapshot '{machine.LastSnapshotId}' not found" });

        int openAlertCount = await _db.Alerts
            .CountAsync(a => a.MachineId == machineId && a.State == AlertState.Open, ct);

        MachineSummaryDto summary = BuildSummary(machine, snapshot, openAlertCount);

        return Ok(summary);
    }

    // ── GET /api/v1/machines/{machineId}/snapshots ────────────────────────────

    /// <summary>
    /// Returns a paged, newest-first list of snapshot metadata for a machine.
    /// Does NOT include RawJson; use GET /api/v1/snapshots/{snapshotId} for the full payload.
    /// GET /api/v1/machines/{machineId}/snapshots?page=1&pageSize=20
    /// </summary>
    [HttpGet("{machineId}/snapshots")]
    [ProducesResponseType(typeof(SnapshotsPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSnapshots(
        string machineId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var customer = (CustomerEntity)HttpContext.Items["Customer"]!;

        bool machineExists = await _db.Machines
            .AnyAsync(m => m.MachineId == machineId && m.CustomerId == customer.CustomerId, ct);

        if (!machineExists)
            return NotFound(new { error = $"machine '{machineId}' not found" });

        int totalCount = await _db.Snapshots
            .CountAsync(s => s.MachineId == machineId, ct);

        List<SnapshotEntity> items = await _db.Snapshots
            .Where(s => s.MachineId == machineId)
            .OrderByDescending(s => s.CollectedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Attach per-snapshot change counts
        var snapshotIds = items.Select(s => s.SnapshotId).ToList();
        Dictionary<Guid, int> changeCounts = await _db.Changes
            .Where(c => snapshotIds.Contains(c.SnapshotId))
            .GroupBy(c => c.SnapshotId)
            .Select(g => new { SnapshotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SnapshotId, x => x.Count, ct);

        return Ok(new SnapshotsPageDto
        {
            MachineId  = machineId,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize,
            Items      = items.ConvertAll(s => new SnapshotMetaDto
            {
                SnapshotId     = s.SnapshotId,
                CollectedAtUtc = s.CollectedAtUtc,
                ReceivedAtUtc  = s.ReceivedAtUtc,
                AgentVersion   = s.AgentVersion,
                OsCaption      = s.OsCaption,
                YarpaVersion   = s.YarpaVersion,
                RamTotalMb     = s.RamTotalMb,
                MinFreeDiskPct = s.MinFreeDiskPercent,
                SqlInstalled   = s.SqlInstalled,
                ChangeCount    = changeCounts.GetValueOrDefault(s.SnapshotId, 0)
            })
        });
    }

    // ── GET /api/v1/machines/{machineId}/changes ──────────────────────────────

    /// <summary>
    /// Returns a paged, newest-first timeline of detected changes for a machine.
    /// GET /api/v1/machines/{machineId}/changes?page=1&pageSize=50
    /// </summary>
    [HttpGet("{machineId}/changes")]
    [ProducesResponseType(typeof(ChangesPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChanges(
        string machineId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var customer = (CustomerEntity)HttpContext.Items["Customer"]!;

        bool machineExists = await _db.Machines
            .AnyAsync(m => m.MachineId == machineId && m.CustomerId == customer.CustomerId, ct);

        if (!machineExists)
            return NotFound(new { error = $"machine '{machineId}' not found" });

        int totalCount = await _db.Changes
            .CountAsync(c => c.MachineId == machineId, ct);

        List<ChangeEntity> items = await _db.Changes
            .Where(c => c.MachineId == machineId)
            .OrderByDescending(c => c.DetectedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new ChangesPageDto
        {
            MachineId  = machineId,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize,
            Items      = items.ConvertAll(c => new ChangeDto
            {
                ChangeId      = c.ChangeId,
                ChangeType    = c.ChangeType,
                SectionName   = c.SectionName,
                OldValue      = c.OldValue,
                NewValue      = c.NewValue,
                DetectedAtUtc = c.DetectedAtUtc,
                SnapshotId    = c.SnapshotId
            })
        });
    }

    // ── GET /api/v1/machines/{machineId}/alerts ───────────────────────────────

    /// <summary>
    /// Returns a paged list of alerts for a machine, ordered by severity (critical first)
    /// then newest-first. GET /api/v1/machines/{machineId}/alerts?state=open&page=1&pageSize=50
    /// The state filter accepts "open" (default), "resolved", or "all".
    /// </summary>
    [HttpGet("{machineId}/alerts")]
    [ProducesResponseType(typeof(AlertsPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAlerts(
        string machineId,
        [FromQuery] string state = "open",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        string normalizedState = (state ?? "open").Trim().ToLowerInvariant();

        var customer = (CustomerEntity)HttpContext.Items["Customer"]!;

        bool machineExists = await _db.Machines
            .AnyAsync(m => m.MachineId == machineId && m.CustomerId == customer.CustomerId, ct);

        if (!machineExists)
            return NotFound(new { error = $"machine '{machineId}' not found" });

        IQueryable<AlertEntity> query = _db.Alerts.Where(a => a.MachineId == machineId);

        query = normalizedState switch
        {
            "open"     => query.Where(a => a.State == AlertState.Open),
            "resolved" => query.Where(a => a.State == AlertState.Resolved),
            _          => query
        };

        int totalCount = await query.CountAsync(ct);

        List<AlertEntity> items = await query
            .OrderBy(a => a.Severity == AlertSeverity.Critical ? 0
                        : a.Severity == AlertSeverity.Warning  ? 1
                        : a.Severity == AlertSeverity.Info     ? 2 : 3)
            .ThenByDescending(a => a.CreatedAtUtc)
            .ThenByDescending(a => a.AlertId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new AlertsPageDto
        {
            MachineId  = machineId,
            State      = normalizedState,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize,
            Items      = items.ConvertAll(a => new AlertDto
            {
                AlertId          = a.AlertId,
                AlertType        = a.AlertType,
                Severity         = a.Severity,
                Message          = a.Message,
                State            = a.State,
                CreatedAtUtc     = a.CreatedAtUtc,
                ResolvedAtUtc    = a.ResolvedAtUtc,
                SourceSnapshotId = a.SourceSnapshotId,
                SourceChangeId   = a.SourceChangeId
            })
        });
    }

    // ── Summary parser ────────────────────────────────────────────────────────

    private static MachineSummaryDto BuildSummary(
        MachineEntity machine,
        SnapshotEntity snapshot,
        int openAlertCount)
    {
        using JsonDocument doc = JsonDocument.Parse(snapshot.RawJson);
        JsonElement root = doc.RootElement;

        JsonElement? GetSection(string name)
        {
            if (root.TryGetProperty("sections", out var sections)
                && sections.TryGetProperty(name, out var sec)
                && sec.TryGetProperty("status", out var st)
                && st.GetString() is "ok" or "partial"
                && sec.TryGetProperty("data", out var data))
                return data;
            return null;
        }

        // OS
        OsSummary? os = null;
        if (GetSection("os") is { } osEl)
        {
            os = new OsSummary
            {
                Caption      = TryGetString(osEl, "caption"),
                Version      = TryGetString(osEl, "version"),
                Build        = TryGetString(osEl, "build"),
                Edition      = TryGetString(osEl, "edition"),
                Architecture = TryGetString(osEl, "architecture")
            };
        }

        // Yarpa version
        YarpaVersionSummary? yarpa = null;
        if (GetSection("yarpaVersion") is { } yvEl)
        {
            yarpa = new YarpaVersionSummary
            {
                Product    = TryGetString(yvEl, "product"),
                Version    = TryGetString(yvEl, "version"),
                DetectedBy = TryGetString(yvEl, "detectedBy")
            };
        }

        // Hardware / RAM / CPU
        HardwareSummary? hardware = null;
        if (GetSection("hardware") is { } hwEl)
        {
            string? cpuName  = null;
            int?    cpuCores = null;
            int?    cpuLogical = null;
            if (hwEl.TryGetProperty("cpu", out var cpuEl))
            {
                cpuName    = TryGetString(cpuEl, "name");
                cpuCores   = cpuEl.TryGetProperty("cores",   out var c) && c.TryGetInt32(out int cv) ? cv : null;
                cpuLogical = cpuEl.TryGetProperty("logical", out var l) && l.TryGetInt32(out int lv) ? lv : null;
            }

            hardware = new HardwareSummary
            {
                Manufacturer = TryGetString(hwEl, "manufacturer"),
                Model        = TryGetString(hwEl, "model"),
                RamTotalMb   = hwEl.TryGetProperty("ramTotalMb", out var ram)
                                && ram.TryGetInt64(out long ramVal) ? ramVal : null,
                RamModules   = hwEl.TryGetProperty("ramModules", out var mods)
                                && mods.TryGetInt32(out int modsVal) ? modsVal : null,
                CpuName      = cpuName,
                CpuCores     = cpuCores,
                CpuLogical   = cpuLogical
            };
        }

        // Disks
        List<DiskSummary>? disks = null;
        if (GetSection("disks") is { } disksEl && disksEl.ValueKind == JsonValueKind.Array)
        {
            disks = new List<DiskSummary>();
            foreach (JsonElement d in disksEl.EnumerateArray())
            {
                disks.Add(new DiskSummary
                {
                    Drive       = TryGetString(d, "drive"),
                    SizeGb      = d.TryGetProperty("sizeGb", out var sg) && sg.TryGetDouble(out double sgv) ? sgv : null,
                    FreeGb      = d.TryGetProperty("freeGb", out var fg) && fg.TryGetDouble(out double fgv) ? fgv : null,
                    FreePercent = d.TryGetProperty("freePercent", out var fp) && fp.TryGetDouble(out double fpv) ? fpv : null,
                    MediaType   = TryGetString(d, "mediaType")
                });
            }
        }

        // SQL Server
        SqlServerSummary? sql = null;
        if (GetSection("sqlServer") is { } sqlEl)
        {
            var instances = new List<SqlInstanceSummary>();
            if (sqlEl.TryGetProperty("instances", out var instArr) && instArr.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement inst in instArr.EnumerateArray())
                {
                    instances.Add(new SqlInstanceSummary
                    {
                        Name         = TryGetString(inst, "name"),
                        Version      = TryGetString(inst, "version"),
                        ServiceState = TryGetString(inst, "serviceState")
                    });
                }
            }
            sql = new SqlServerSummary
            {
                Installed = sqlEl.TryGetProperty("installed", out var instProp) && instProp.GetBoolean(),
                Instances = instances
            };
        }

        // Payment terminals
        List<PaymentTerminalSummary>? terminals = null;
        if (GetSection("paymentTerminals") is { } ptEl && ptEl.ValueKind == JsonValueKind.Array)
        {
            terminals = new List<PaymentTerminalSummary>();
            foreach (JsonElement t in ptEl.EnumerateArray())
            {
                terminals.Add(new PaymentTerminalSummary
                {
                    Vendor  = TryGetString(t, "vendor"),
                    Model   = TryGetString(t, "model"),
                    ComPort = TryGetString(t, "comPort"),
                    Vid     = TryGetString(t, "vid"),
                    Pid     = TryGetString(t, "pid")
                });
            }
        }

        // Printers
        List<PrinterSummary>? printers = null;
        if (GetSection("printers") is { } prEl && prEl.ValueKind == JsonValueKind.Array)
        {
            printers = new List<PrinterSummary>();
            foreach (JsonElement p in prEl.EnumerateArray())
            {
                printers.Add(new PrinterSummary
                {
                    Name      = TryGetString(p, "name"),
                    IsDefault = p.TryGetProperty("isDefault", out var def) && def.GetBoolean(),
                    Status    = TryGetString(p, "status"),
                    PortName  = TryGetString(p, "portName"),
                    Driver    = TryGetString(p, "driver")
                });
            }
        }

        // USB devices (scanners / barcode readers and other peripherals)
        List<UsbDeviceSummary>? usbDevices = null;
        if (GetSection("usbDevices") is { } usbEl && usbEl.ValueKind == JsonValueKind.Array)
        {
            usbDevices = new List<UsbDeviceSummary>();
            foreach (JsonElement u in usbEl.EnumerateArray())
            {
                usbDevices.Add(new UsbDeviceSummary
                {
                    Name         = TryGetString(u, "name"),
                    Vid          = TryGetString(u, "vid"),
                    Pid          = TryGetString(u, "pid"),
                    DeviceClass  = TryGetString(u, "deviceClass"),
                    Manufacturer = TryGetString(u, "manufacturer")
                });
            }
        }

        // System info (domain, uptime)
        SystemInfoSummary? sysInfo = null;
        if (GetSection("system") is { } sysEl)
        {
            sysInfo = new SystemInfoSummary
            {
                UserName      = TryGetString(sysEl, "userName"),
                Domain        = TryGetString(sysEl, "domainOrWorkgroup"),
                UptimeSeconds = sysEl.TryGetProperty("uptimeSeconds", out var up)
                                && up.TryGetInt64(out long upVal) ? upVal : null
            };
        }

        // Network (adapters, IP, MAC, gateway, DNS)
        NetworkSummary? network = null;
        if (GetSection("network") is { } netEl
            && netEl.TryGetProperty("adapters", out var adaptersEl)
            && adaptersEl.ValueKind == JsonValueKind.Array)
        {
            var adapters = new List<NetworkAdapterSummary>();
            foreach (JsonElement a in adaptersEl.EnumerateArray())
            {
                var dns = new List<string>();
                if (a.TryGetProperty("dns", out var dnsArr) && dnsArr.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement d in dnsArr.EnumerateArray())
                        if (d.GetString() is { } dnsEntry) dns.Add(dnsEntry);

                adapters.Add(new NetworkAdapterSummary
                {
                    Name    = TryGetString(a, "name"),
                    Mac     = TryGetString(a, "mac"),
                    IPv4    = TryGetString(a, "ipv4"),
                    Gateway = TryGetString(a, "gateway"),
                    Dns     = dns.Count > 0 ? dns.ToArray() : null
                });
            }
            network = new NetworkSummary { Adapters = adapters };
        }

        // COM ports
        List<ComPortSummary>? comPorts = null;
        if (GetSection("comPorts") is { } comEl && comEl.ValueKind == JsonValueKind.Array)
        {
            comPorts = new List<ComPortSummary>();
            foreach (JsonElement c in comEl.EnumerateArray())
            {
                comPorts.Add(new ComPortSummary
                {
                    Port       = TryGetString(c, "port"),
                    DeviceName = TryGetString(c, "deviceName")
                });
            }
        }

        // Recent event log entries (max 10, errors and warnings first)
        List<EventLogEntrySummary>? eventLogs = null;
        if (GetSection("eventLogs") is { } evEl && evEl.ValueKind == JsonValueKind.Array)
        {
            eventLogs = new List<EventLogEntrySummary>();
            int taken = 0;
            foreach (JsonElement e in evEl.EnumerateArray())
            {
                if (taken >= 10) break;
                eventLogs.Add(new EventLogEntrySummary
                {
                    Log     = TryGetString(e, "log"),
                    Source  = TryGetString(e, "source"),
                    EventId = e.TryGetProperty("eventId", out var eid) && eid.TryGetInt32(out int eidVal) ? eidVal : null,
                    Level   = TryGetString(e, "level"),
                    TimeUtc = e.TryGetProperty("timeUtc", out var tProp)
                              && tProp.TryGetDateTimeOffset(out DateTimeOffset dto)
                              ? dto.UtcDateTime : null,
                    Message = TryGetString(e, "message")
                });
                taken++;
            }
        }

        // Installed software
        List<InstalledSoftwareItemSummary>? software = null;
        if (GetSection("installedSoftware") is { } swEl && swEl.ValueKind == JsonValueKind.Array)
        {
            software = new List<InstalledSoftwareItemSummary>();
            foreach (JsonElement s in swEl.EnumerateArray())
            {
                software.Add(new InstalledSoftwareItemSummary
                {
                    Name        = TryGetString(s, "name"),
                    Version     = TryGetString(s, "version"),
                    Publisher   = TryGetString(s, "publisher"),
                    InstallDate = TryGetString(s, "installDate")
                });
            }
        }

        return new MachineSummaryDto
        {
            MachineId         = machine.MachineId,
            ComputerName      = machine.ComputerName,
            FirstSeenUtc      = machine.FirstSeenUtc,
            LastSeenUtc       = machine.LastSeenUtc,
            LastSnapshotId    = snapshot.SnapshotId,
            CollectedAtUtc    = snapshot.CollectedAtUtc,
            Os                = os,
            YarpaVersion      = yarpa,
            Hardware          = hardware,
            Disks             = disks,
            SqlServer         = sql,
            PaymentTerminals  = terminals,
            Printers          = printers,
            UsbDevices        = usbDevices,
            SystemInfo        = sysInfo,
            Network           = network,
            ComPorts          = comPorts,
            RecentEventLogs   = eventLogs,
            InstalledSoftware = software,
            OpenAlertCount    = openAlertCount
        };
    }

    private static string? TryGetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var p) ? p.GetString() : null;
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

// Machines list
public sealed class MachinesPageDto
{
    public int TotalCount { get; init; }
    public int Page       { get; init; }
    public int PageSize   { get; init; }
    public List<MachineListItemDto> Items { get; init; } = new();
}

public sealed class MachineListItemDto
{
    public string    MachineId      { get; init; } = string.Empty;
    public string    ComputerName   { get; init; } = string.Empty;
    public DateTime  FirstSeenUtc   { get; init; }
    public DateTime  LastSeenUtc    { get; init; }
    public Guid?     LastSnapshotId { get; init; }
    public int       OpenAlertCount { get; init; }
}

// Summary
public sealed class MachineSummaryDto
{
    public string    MachineId      { get; init; } = string.Empty;
    public string    ComputerName   { get; init; } = string.Empty;
    public DateTime  FirstSeenUtc   { get; init; }
    public DateTime  LastSeenUtc    { get; init; }
    public Guid?     LastSnapshotId { get; init; }
    public DateTime? CollectedAtUtc { get; init; }
    public int       OpenAlertCount { get; init; }

    public OsSummary?                        Os                { get; init; }
    public YarpaVersionSummary?              YarpaVersion      { get; init; }
    public HardwareSummary?                  Hardware          { get; init; }
    public List<DiskSummary>?                Disks             { get; init; }
    public SqlServerSummary?                 SqlServer         { get; init; }
    public List<PaymentTerminalSummary>?     PaymentTerminals  { get; init; }
    public List<PrinterSummary>?             Printers          { get; init; }
    public List<UsbDeviceSummary>?           UsbDevices        { get; init; }
    public SystemInfoSummary?                SystemInfo        { get; init; }
    public NetworkSummary?                   Network           { get; init; }
    public List<ComPortSummary>?             ComPorts          { get; init; }
    public List<EventLogEntrySummary>?       RecentEventLogs   { get; init; }
    public List<InstalledSoftwareItemSummary>? InstalledSoftware { get; init; }
}

public sealed class OsSummary
{
    public string? Caption      { get; init; }
    public string? Version      { get; init; }
    public string? Build        { get; init; }
    public string? Edition      { get; init; }
    public string? Architecture { get; init; }
}

public sealed class YarpaVersionSummary
{
    public string? Product    { get; init; }
    public string? Version    { get; init; }
    public string? DetectedBy { get; init; }
}

public sealed class HardwareSummary
{
    public string? Manufacturer { get; init; }
    public string? Model        { get; init; }
    public long?   RamTotalMb   { get; init; }
    public int?    RamModules   { get; init; }
    public string? CpuName      { get; init; }
    public int?    CpuCores     { get; init; }
    public int?    CpuLogical   { get; init; }
}

public sealed class DiskSummary
{
    public string? Drive       { get; init; }
    public double? SizeGb      { get; init; }
    public double? FreeGb      { get; init; }
    public double? FreePercent { get; init; }
    public string? MediaType   { get; init; }
}

public sealed class SqlServerSummary
{
    public bool                       Installed { get; init; }
    public List<SqlInstanceSummary>   Instances { get; init; } = new();
}

public sealed class SqlInstanceSummary
{
    public string? Name         { get; init; }
    public string? Version      { get; init; }
    public string? ServiceState { get; init; }
}

public sealed class PaymentTerminalSummary
{
    public string? Vendor  { get; init; }
    public string? Model   { get; init; }
    public string? ComPort { get; init; }
    public string? Vid     { get; init; }
    public string? Pid     { get; init; }
}

public sealed class PrinterSummary
{
    public string? Name      { get; init; }
    public bool    IsDefault { get; init; }
    public string? Status    { get; init; }
    public string? PortName  { get; init; }
    public string? Driver    { get; init; }
}

public sealed class UsbDeviceSummary
{
    public string? Name         { get; init; }
    public string? Vid          { get; init; }
    public string? Pid          { get; init; }
    public string? DeviceClass  { get; init; }
    public string? Manufacturer { get; init; }
}

public sealed class SystemInfoSummary
{
    public string? UserName      { get; init; }
    public string? Domain        { get; init; }
    public long?   UptimeSeconds { get; init; }
}

public sealed class NetworkSummary
{
    public List<NetworkAdapterSummary> Adapters { get; init; } = new();
}

public sealed class NetworkAdapterSummary
{
    public string?   Name    { get; init; }
    public string?   Mac     { get; init; }
    public string?   IPv4    { get; init; }
    public string?   Gateway { get; init; }
    public string[]? Dns     { get; init; }
}

public sealed class ComPortSummary
{
    public string? Port       { get; init; }
    public string? DeviceName { get; init; }
}

public sealed class EventLogEntrySummary
{
    public string?   Log     { get; init; }
    public string?   Source  { get; init; }
    public int?      EventId { get; init; }
    public string?   Level   { get; init; }
    public DateTime? TimeUtc { get; init; }
    public string?   Message { get; init; }
}

public sealed class InstalledSoftwareItemSummary
{
    public string? Name        { get; init; }
    public string? Version     { get; init; }
    public string? Publisher   { get; init; }
    public string? InstallDate { get; init; }
}

// Snapshots list
public sealed class SnapshotsPageDto
{
    public string MachineId  { get; init; } = string.Empty;
    public int    TotalCount { get; init; }
    public int    Page       { get; init; }
    public int    PageSize   { get; init; }
    public List<SnapshotMetaDto> Items { get; init; } = new();
}

public sealed class SnapshotMetaDto
{
    public Guid     SnapshotId     { get; init; }
    public DateTime CollectedAtUtc { get; init; }
    public DateTime ReceivedAtUtc  { get; init; }
    public string   AgentVersion   { get; init; } = string.Empty;
    public string?  OsCaption      { get; init; }
    public string?  YarpaVersion   { get; init; }
    public long?    RamTotalMb     { get; init; }
    public double?  MinFreeDiskPct { get; init; }
    public bool?    SqlInstalled   { get; init; }
    public int      ChangeCount    { get; init; }
}

// Changes (Timeline)
public sealed class ChangesPageDto
{
    public string MachineId  { get; init; } = string.Empty;
    public int    TotalCount { get; init; }
    public int    Page       { get; init; }
    public int    PageSize   { get; init; }
    public List<ChangeDto> Items { get; init; } = new();
}

public sealed class ChangeDto
{
    public long     ChangeId      { get; init; }
    public string   ChangeType    { get; init; } = string.Empty;
    public string   SectionName   { get; init; } = string.Empty;
    public string?  OldValue      { get; init; }
    public string?  NewValue      { get; init; }
    public DateTime DetectedAtUtc { get; init; }
    public Guid     SnapshotId    { get; init; }
}

// Alerts
public sealed class AlertsPageDto
{
    public string MachineId  { get; init; } = string.Empty;
    public string State      { get; init; } = string.Empty;
    public int    TotalCount { get; init; }
    public int    Page       { get; init; }
    public int    PageSize   { get; init; }
    public List<AlertDto> Items { get; init; } = new();
}

public sealed class AlertDto
{
    public long      AlertId          { get; init; }
    public string    AlertType        { get; init; } = string.Empty;
    public string    Severity         { get; init; } = string.Empty;
    public string    Message          { get; init; } = string.Empty;
    public string    State            { get; init; } = string.Empty;
    public DateTime  CreatedAtUtc     { get; init; }
    public DateTime? ResolvedAtUtc    { get; init; }
    public Guid?     SourceSnapshotId { get; init; }
    public long?     SourceChangeId   { get; init; }
}
