using Microsoft.AspNetCore.Mvc;
using Yarpa.Api.Services.Alerts;
using Yarpa.Api.Services.Retention;

namespace Yarpa.Api.Controllers;

/// <summary>
/// Internal maintenance endpoints. Requires a valid X-Api-Key (enforced by ApiKeyMiddleware).
/// Used to trigger the time-based NoRecentContact scan and the retention job on demand
/// (e.g. from tests or an external scheduler) in addition to the periodic background services.
/// </summary>
[ApiController]
[Route("api/v1/internal")]
public sealed class AlertsMaintenanceController : ControllerBase
{
    private readonly INoRecentContactChecker _checker;
    private readonly IRetentionService _retention;

    public AlertsMaintenanceController(
        INoRecentContactChecker checker,
        IRetentionService retention)
    {
        _checker = checker;
        _retention = retention;
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

    /// <summary>
    /// Runs the snapshot retention job once. No-op (Deleted = 0) when retention is disabled.
    /// POST /api/v1/internal/retention/run
    /// </summary>
    [HttpPost("retention/run")]
    [ProducesResponseType(typeof(RetentionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunRetention(CancellationToken ct)
    {
        RetentionResult result = await _retention.RunAsync(ct);
        return Ok(result);
    }
}
