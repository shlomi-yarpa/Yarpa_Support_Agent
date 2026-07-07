using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Controllers;

/// <summary>
/// Read endpoints for machines: timeline of changes, and future CRM endpoints.
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

    /// <summary>
    /// Returns a paged, newest-first timeline of detected changes for a machine.
    /// GET /api/v1/machines/{machineId}/changes?page=1&amp;pageSize=50
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

        // Verify the machine exists under the authenticated customer
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

        var dto = new ChangesPageDto
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
        };

        return Ok(dto);
    }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

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
