using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Yarpa.Api.Data;

using Yarpa.Api.Data.Entities;
using Yarpa.Api.Services;
using Yarpa.Contracts;

namespace Yarpa.Api.Controllers;

/// <summary>
/// Handles inbound diagnostic snapshots from the Yarpa Agent (POST)
/// and retrieval of stored snapshots for the CRM Dashboard (GET).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public sealed class SnapshotsController : ControllerBase
{
    private readonly YarpaDbContext _db;

    public SnapshotsController(YarpaDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the full raw JSON of a stored snapshot.
    /// Authorization: the snapshot's machine must belong to the authenticated customer.
    /// GET /api/v1/snapshots/{snapshotId}
    /// </summary>
    [HttpGet("{snapshotId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSnapshot(
        Guid snapshotId,
        CancellationToken ct)
    {
        var customer = (CustomerEntity)HttpContext.Items["Customer"]!;

        // Verify ownership: snapshot → machine → customer
        SnapshotEntity? snapshot = await _db.Snapshots
            .Include(s => s.Machine)
            .FirstOrDefaultAsync(s => s.SnapshotId == snapshotId, ct);

        if (snapshot == null || snapshot.Machine.CustomerId != customer.CustomerId)
            return NotFound(new { error = $"snapshot '{snapshotId}' not found" });

        // Return the stored raw JSON verbatim — no re-serialisation, no dispose issue.
        return Content(snapshot.RawJson, "application/json");
    }

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Accepts a <see cref="DiagnosticsSnapshot"/> from the Agent.
    /// The body is received as a raw <see cref="JsonElement"/> to capture
    /// RawJson for storage while still deserializing for validation.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PostSnapshot(
        [FromBody] JsonElement body,
        [FromServices] IValidator<DiagnosticsSnapshot> validator,
        [FromServices] IClientResolver clientResolver,
        [FromServices] ISnapshotStore snapshotStore,
        CancellationToken ct)
    {
        // Keep the raw JSON for verbatim storage
        string rawJson = body.GetRawText();

        // Deserialize and validate
        DiagnosticsSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<DiagnosticsSnapshot>(rawJson, DeserializeOptions);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"JSON לא תקין: {ex.Message}" });
        }

        if (snapshot == null)
            return BadRequest(new { error = "גוף הבקשה ריק" });

        var validation = await validator.ValidateAsync(snapshot, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                error = "שגיאת ולידציה",
                details = validation.Errors.Select(e => e.ErrorMessage)
            });
        }

        // Resolve (or register) the machine under the authenticated customer
        var customer = (CustomerEntity)HttpContext.Items["Customer"]!;

        string computerName = TryGetComputerName(snapshot);
        MachineEntity machine = await clientResolver.ResolveOrRegisterAsync(
            snapshot.MachineId, computerName, customer, ct);

        // Persist (idempotency handled inside the store)
        SnapshotStoreResult result = await snapshotStore.StoreAsync(
            snapshot, rawJson, machine, ct);

        var responseBody = new
        {
            snapshotId = result.SnapshotId,
            machineId = result.MachineId,
            changes = result.Changes,
            alerts = result.Alerts
        };

        return result.IsNew
            ? Accepted(responseBody)
            : Ok(responseBody);
    }

    private static string TryGetComputerName(DiagnosticsSnapshot snapshot)
    {
        if (snapshot.Sections.TryGetValue("system", out var sys)
            && sys.Data is JsonElement el
            && el.TryGetProperty("computerName", out var cn))
        {
            return cn.GetString() ?? snapshot.MachineId;
        }
        return snapshot.MachineId;
    }
}
