using HIM.AiService.Extensions;
using HIM.AiService.Models.AI;
using HIM.AiService.Security;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// CreateBuilder already loads, in precedence order: appsettings.json, appsettings.{Environment}.json,
// user secrets (Development only), environment variables, command line. Re-adding appsettings.json
// here would append it *after* user secrets and so override them - which silently defeats keeping the
// API keys out of the repo. Environment variables are likewise already in the chain, and re-adding
// them was only needed to undo the re-added JSON file.

// Bind Configuration (Options Pattern - SOLID: Dependency Inversion)
// Fail closed: the service must not start without a shared secret configured.
builder.Services.AddOptions<AiSettings>()
    .Bind(builder.Configuration.GetSection(nameof(AiSettings)))
    .Validate(
        s => !string.IsNullOrWhiteSpace(s.Security.SharedSecret),
        "AiSettings:Security:SharedSecret must be configured. Refusing to start without a shared secret.")
    .ValidateOnStart();

// Add services to the container.
builder.Services.AddServices();

builder.Services.AddControllers();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("ChatAsk", httpContext =>
    {
        var rateLimit = httpContext.RequestServices
            .GetRequiredService<IOptions<AiSettings>>().Value.RateLimit;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimit.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimit.WindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = rateLimit.QueueLimit
            });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var kbService = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseService>();
    _ = kbService.InitializeAsync();
}

// No UseHttpsRedirection: this service is HTTP-only behind the gateway on a private Docker
// bridge, with no HTTPS port configured, so the middleware could only emit a broken 307 (SEC-09).

// Cheap check first: reject unauthenticated requests before they can burn rate-limit quota.
app.UseMiddleware<SharedSecretMiddleware>();

app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();
