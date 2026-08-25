using Microsoft.AspNetCore.Mvc;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Api.Controllers;

[ApiController]
[Route("api/webhook-endpoints")]
public class WebhookEndpointsController : ControllerBase
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<WebhookEndpointsController> _logger;

    public WebhookEndpointsController(
        IDashboardRepository dashboardRepository,
        ICurrentUserContext currentUserContext,
        ILogger<WebhookEndpointsController> logger)
    {
        _dashboardRepository = dashboardRepository;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookEndpointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetWebhookEndpoints(CancellationToken cancellationToken)
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

        var endpoints = await _dashboardRepository.GetWebhookEndpointsAsync(tenantId.Value, cancellationToken);
        return Ok(endpoints);
    }
}
