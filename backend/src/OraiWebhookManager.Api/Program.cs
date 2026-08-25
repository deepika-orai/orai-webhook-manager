using Microsoft.AspNetCore.HttpOverrides;
using OraiWebhookManager.Api.Logging;
using OraiWebhookManager.Api.Middleware;
using OraiWebhookManager.Api.Services;
using OraiWebhookManager.Application.Interfaces;
using OraiWebhookManager.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Webhook key log redaction across all providers and diagnostics
builder.Services.AddWebhookKeyRedactionLogging();

// Add services to the container.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();

// Infrastructure layer (EF Core, Repositories, Workers, Services)
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();

// OpenAPI Document configuration
builder.Services.AddOpenApi();

// Forwarded Headers for Azure / Reverse Proxies
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// CORS configuration
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseForwardedHeaders();

// Webhook key URL/telemetry redaction middleware
app.UseMiddleware<WebhookKeyRedactionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseCors("FrontendCorsPolicy");

app.UseAuthorization();

app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
