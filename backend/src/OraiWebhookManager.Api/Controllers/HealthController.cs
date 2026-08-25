using Microsoft.AspNetCore.Mvc;
using OraiWebhookManager.Api.Models;

namespace OraiWebhookManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        var response = new HealthResponse(
            Status: "healthy",
            Service: "ORAI Webhook Manager API",
            TimestampUtc: DateTimeOffset.UtcNow.ToString("o")
        );

        return Ok(response);
    }
}
