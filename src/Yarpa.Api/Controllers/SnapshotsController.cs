using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Api.Services;
using Yarpa.Contracts;

namespace Yarpa.Api.Controllers;

/// <summary>
/// Handles inbound diagnostic snapshots from the Yarpa Agent.
/// POST /api/v1/snapshots – accepts and persists a snapshot.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public sealed class SnapshotsController : ControllerBase
{
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
