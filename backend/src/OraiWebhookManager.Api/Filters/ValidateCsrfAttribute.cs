using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OraiWebhookManager.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ValidateCsrfAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        var isValid = await antiforgery.IsRequestValidAsync(context.HttpContext);

        if (!isValid)
        {
            context.Result = new BadRequestObjectResult(new
            {
                error = "Antiforgery token validation failed. Please provide a valid X-XSRF-TOKEN header and cookie."
            });
        }
    }
}
