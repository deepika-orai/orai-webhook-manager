using Microsoft.AspNetCore.Mvc;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IDashboardRepository dashboardRepository,
        ICurrentUserContext currentUserContext,
        ILogger<DashboardController> logger)
    {
        _dashboardRepository = dashboardRepository;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var tenantId = _currentUserContext.TenantId;
        if (!tenantId.HasValue)
        {
            return Unauthorized(new { error = "Authentication or tenant context is required." });
        }

        var isTenantActive = await _dashboardRepository.ValidateTenantActiveAsync(tenantId.Value, cancellationToken);
        if (!isTenantActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Tenant does not exist or is inactive." });
        }

        var summary = await _dashboardRepository.GetSummaryAsync(tenantId.Value, cancellationToken);
        return Ok(summary);
    }
}
