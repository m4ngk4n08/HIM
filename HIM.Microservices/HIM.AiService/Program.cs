using HIM.AiService.Extenstions;
using HIM.AiService.Extensions;
using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

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

app.UseHttpsRedirection();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers().AddControllerEndpointFilter<SharedSecretEndpointFilter>();

app.Run();
