using Microsoft.AspNetCore.Mvc;
using Yarpa.Api.Services.Alerts;

namespace Yarpa.Api.Controllers;

/// <summary>
/// Internal maintenance endpoints. Requires a valid X-Api-Key (enforced by ApiKeyMiddleware).
/// Used to trigger the time-based NoRecentContact scan on demand (e.g. from tests or an
/// external scheduler) in addition to the periodic background service.
/// </summary>
[ApiController]
[Route("api/v1/internal")]
public sealed class AlertsMaintenanceController : ControllerBase
{
    private readonly INoRecentContactChecker _checker;

    public AlertsMaintenanceController(INoRecentContactChecker checker)
    {
        _checker = checker;
    }

    /// <summary>
    /// Runs the no-recent-contact scan across all machines, raising or resolving
    /// NoRecentContact alerts as appropriate.
    /// POST /api/v1/internal/alerts/scan-no-recent-contact
    /// </summary>
    [HttpPost("alerts/scan-no-recent-contact")]
    [ProducesResponseType(typeof(NoRecentContactResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ScanNoRecentContact(CancellationToken ct)
    {
        NoRecentContactResult result = await _checker.RunAsync(ct);
        return Ok(result);
    }
}
