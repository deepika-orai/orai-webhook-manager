using Microsoft.AspNetCore.Mvc;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Api.Controllers;

[ApiController]
[Route("api/messages")]
public class MessagesController : ControllerBase
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(
        IDashboardRepository dashboardRepository,
        ICurrentUserContext currentUserContext,
        ILogger<MessagesController> logger)
    {
        _dashboardRepository = dashboardRepository;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<MessageListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessages(
        [FromQuery] MessageFilterParams filter,
        CancellationToken cancellationToken)
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

        var result = await _dashboardRepository.GetMessagesAsync(tenantId.Value, filter, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/events")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageStatusEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessageEvents(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
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

        var events = await _dashboardRepository.GetMessageEventsAsync(tenantId.Value, id, cancellationToken);
        return Ok(events);
    }
}
