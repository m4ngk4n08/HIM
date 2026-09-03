using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = Host.CreateApplicationBuilder(args);

// SEC-06 / BL-2: compose sets LOG_PATH for both services; this used to assign logPath from
// Path.Combine and then test IsNullOrEmpty on the result, which can never be true - dead code
// that looked like it meant to read the env var. Now it actually does, falling back to the old
// local default when unset (dev machines don't set LOG_PATH).
var logPath = Environment.GetEnvironmentVariable("LOG_PATH");
if (string.IsNullOrEmpty(logPath))
    logPath = Path.Combine(AppContext.BaseDirectory, "Logs", "gateway-log-.json");

var logDir = Path.GetDirectoryName(logPath);

// In development, delete old log files to start fresh
if (builder.Environment.IsDevelopment())
{
    if (!string.IsNullOrEmpty(logDir) && Directory.Exists(logDir))
    {
        // Delete all .json log files from previous runs
        foreach (var file in Directory.GetFiles(logDir, "*.json"))
        {
            try { File.Delete(file); } catch { /* ignore */ }
        }
    }
}
builder.Logging.AddSerilog(new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.File(
        new CompactJsonFormatter(),
        logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .MinimumLevel.Information()
    .MinimumLevel.Override("HIM", LogEventLevel.Debug)
    .CreateLogger());

// CreateApplicationBuilder already loads, in precedence order: appsettings.json,
// appsettings.{Environment}.json, user secrets (Development only), environment variables, command
// line. Re-adding appsettings.json here would append it *after* user secrets and override them,
// which silently defeats keeping the shared secret out of the repo. Environment variables are
// already in the chain and still win, so the re-add they needed is gone too.

builder.Services.Configure<SshSettings>(builder.Configuration.GetSection("SshSettings"));
builder.Services.Configure<AiServiceSettings>(builder.Configuration.GetSection("AiServiceSettings"));
builder.Services.Configure<KnowledgeBaseSettings>(builder.Configuration.GetSection("KnowledgeBaseSettings"));
builder.Services.Configure<GameSettings>(builder.Configuration.GetSection("GameSettings"));

builder.Services.AddService();

builder.ConfigureContainer(new DefaultServiceProviderFactory(ServiceExtensions.ContainerValidationOptions));

// Resilient AI Client(Typed HttpClient Pattern)
// Used AddHttpClient with a Retry Policy to handle transient network errors.
builder.Services.AddHttpClient<IAiClientService, AiClientService>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<AiServiceSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
})
.AddPolicyHandler(HttpPolicyExtensions
.HandleTransientHttpError() // Handles 5xx and 408
.WaitAndRetryAsync(3, retryAttemps => TimeSpan.FromSeconds(Math.Pow(2, retryAttemps))));

using IHost host = builder.Build();

// 4. Lifecycle & Graceful Shutdown Logic
// Create a global CancellationTokenSource to orchestrate a clean exit across all async workers.
using var cts = new CancellationTokenSource();

// Intercept Ctrl+C / SIGINT to trigger the cancellation engine
Console.CancelKeyPress += (s, e) =>
{
    Console.WriteLine("\n[System] Shutdown signal received. Closing all active SSH sessions...");
    cts.Cancel();
    e.Cancel = true; // Prevent immediate process termination
};

// 5. Execution Phase
// Retrieve the listener from the DI container and launch the gateway.
var listener = host.Services.GetRequiredService<ISshServerListener>();

try
{
    // Start the listener in a non-blocking way
    var listenerTask = listener.StartAsync(cts.Token);

    await Task.WhenAny(listenerTask, host.RunAsync(cts.Token));
}
catch (OperationCanceledException)
{
    // Expected behavior during a graceful shutdown
    Console.WriteLine("[System] Gateway shutdown complete.");
}
catch (Exception ex)
{
    // Fatal error boundary for the entire Gateway application
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[Fatal] Gateway crashed: {ex.Message}");
    Console.ResetColor();
}