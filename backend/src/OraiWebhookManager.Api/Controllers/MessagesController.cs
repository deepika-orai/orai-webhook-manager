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
    private readonly IWebhookEndpointResolver _endpointResolver;
    private readonly ITemplateMappingRepository _templateMappingRepository;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(
        IDashboardRepository dashboardRepository,
        ICurrentUserContext currentUserContext,
        IWebhookEndpointResolver endpointResolver,
        ITemplateMappingRepository templateMappingRepository,
        ILogger<MessagesController> logger)
    {
        _dashboardRepository = dashboardRepository;
        _currentUserContext = currentUserContext;
        _endpointResolver = endpointResolver;
        _templateMappingRepository = templateMappingRepository;
        _logger = logger;
    }

    [HttpPost("{webhookKey}/mappings")]
    [ProducesResponseType(typeof(TemplateMappingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TemplateMappingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TemplateMappingErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(TemplateMappingErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TemplateMappingErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMapping(
        [FromRoute] string webhookKey,
        [FromBody] CreateTemplateMappingRequest request,
        CancellationToken cancellationToken)
    {
        var resolution = await _endpointResolver.ResolveEndpointAsync(webhookKey, cancellationToken);
        if (resolution.Status != WebhookEndpointResolutionStatus.Success || resolution.Endpoint == null)
        {
            return NotFound(new TemplateMappingErrorResponse(false, "Webhook endpoint not found or inactive."));
        }

        if (request == null || !ModelState.IsValid)
        {
            return BadRequest(new TemplateMappingErrorResponse(false, "Invalid template mapping request payload."));
        }

        if (request.Template == null || string.IsNullOrWhiteSpace(request.Template.Name) || string.IsNullOrWhiteSpace(request.Template.Language))
        {
            return BadRequest(new TemplateMappingErrorResponse(false, "Template name and language are required."));
        }

        if (!request.SentAt.HasValue)
        {
            return BadRequest(new TemplateMappingErrorResponse(false, "SentAt timestamp is required."));
        }

        var result = await _templateMappingRepository.CreateOrEnrichMappingAsync(resolution.Endpoint, request, cancellationToken);

        return result.Status switch
        {
            TemplateMappingStatus.Created => StatusCode(StatusCodes.Status201Created, new TemplateMappingResponse(true, true, result.Data!)),
            TemplateMappingStatus.ExistingMatch => Ok(new TemplateMappingResponse(true, false, result.Data!)),
            TemplateMappingStatus.Conflict => StatusCode(StatusCodes.Status409Conflict, new TemplateMappingErrorResponse(false, result.ErrorMessage ?? "A different mapping already exists for this message ID.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new TemplateMappingErrorResponse(false, "An unexpected error occurred."))
        };
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
