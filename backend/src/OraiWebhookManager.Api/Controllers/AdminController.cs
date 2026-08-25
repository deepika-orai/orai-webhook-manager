using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OraiWebhookManager.Api.Filters;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Application.Models;

namespace OraiWebhookManager.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "PlatformAdmin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ICurrentUserContext _currentUserContext;

    public AdminController(
        IAdminService adminService,
        ICurrentUserContext currentUserContext)
    {
        _adminService = adminService;
        _currentUserContext = currentUserContext;
    }

    [HttpGet("platform/summary")]
    public async Task<IActionResult> GetPlatformSummary(CancellationToken cancellationToken)
    {
        var summary = await _adminService.GetPlatformSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new AdminTenantFilterParams(search, isActive, page, pageSize);
        var result = await _adminService.GetTenantsAsync(filter, cancellationToken);
        return Ok(result);
    }

    [HttpGet("tenants/{id:guid}/summary", Name = nameof(GetTenantSummary))]
    public async Task<IActionResult> GetTenantSummary([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var summary = await _adminService.GetTenantSummaryAsync(id, cancellationToken);
        if (summary == null)
        {
            return NotFound(new { error = $"Tenant with ID {id} not found" });
        }

        return Ok(summary);
    }

    [HttpPost("tenants")]
    [ValidateCsrf]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = _currentUserContext.UserId ?? Guid.Empty;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await _adminService.CreateTenantAsync(request, adminUserId, ipAddress, cancellationToken);
            return Created($"/api/admin/tenants/{result.TenantId}/summary", result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("tenants/{id:guid}/status")]
    [ValidateCsrf]
    public async Task<IActionResult> UpdateTenantStatus(
        [FromRoute] Guid id,
        [FromBody] UpdateTenantStatusRequest request,
        CancellationToken cancellationToken)
    {
        var adminUserId = _currentUserContext.UserId ?? Guid.Empty;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var success = await _adminService.UpdateTenantStatusAsync(id, request.IsActive, adminUserId, ipAddress, cancellationToken);
        if (!success)
        {
            return NotFound(new { error = $"Tenant with ID {id} not found" });
        }

        return Ok(new { succeeded = true, message = $"Tenant status updated to {(request.IsActive ? "Active" : "Suspended")}" });
    }

    [HttpPost("tenants/{id:guid}/reset-client-password")]
    [ValidateCsrf]
    public async Task<IActionResult> ResetClientPassword([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var adminUserId = _currentUserContext.UserId ?? Guid.Empty;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await _adminService.ResetClientPasswordAsync(id, adminUserId, ipAddress, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("webhook-endpoints/{id:guid}/rotate-key")]
    [ValidateCsrf]
    public async Task<IActionResult> RotateWebhookKey([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var adminUserId = _currentUserContext.UserId ?? Guid.Empty;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await _adminService.RotateWebhookKeyAsync(id, adminUserId, ipAddress, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
