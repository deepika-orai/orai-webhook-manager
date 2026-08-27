using Microsoft.AspNetCore.Mvc;
using OraiWebhookManager.Application.Helpers;
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
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
        if (events == null)
        {
            return NotFound(new { error = "Message not found." });
        }

        return Ok(events);
    }

    [HttpGet("export")]
    [Produces("text/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportStatusLogsCsv(
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

        var logs = await _dashboardRepository.GetStatusLogsForExportAsync(tenantId.Value, filter, cancellationToken);
        var csvBytes = CsvExportHelper.GenerateStatusLogsCsvBytes(logs);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var filename = $"whatsapp_status_logs_{tenantId.Value:N}_{timestamp}.csv";

        return File(csvBytes, "text/csv; charset=utf-8", filename);
    }
}
