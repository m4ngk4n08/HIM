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

// 1. Initialize the Application Builder
// We use the Host.CreateApplicationBuilder to leverage built-in logging,
// configuration, and dependency injection in a streamlined .NET 10 pattern.
var builder = Host.CreateApplicationBuilder(args);

// 2. Configuration Management
// Load environment-specific settings and ensure appsettings.json is required.
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// 3. Service Registration (Dependency Injection)
// Bind the SSH Settings from the configuration file to the SshSettings model.
builder.Services.Configure<SshSettings>(builder.Configuration.GetSection("SshSettings"));
builder.Services.Configure<AiServiceSettings>(builder.Configuration.GetSection("AiServiceSettings"));

// Register infrastructure services as Singletons to maintain state (keys/auth/listener)
builder.Services.AddSingleton<IHostKeyService, HostKeyService>();
builder.Services.AddSingleton<IAuthenticationService, GuestAuthenticationService>();
builder.Services.AddSingleton<ISshServerListener, SshServerListener>();
builder.Services.AddSingleton<ITuiEngine, TuiEngine>();
builder.Services.AddSingleton<ICommandService, CommandService>();
builder.Services.AddSingleton<IConsoleEngineService, ConsoleEngineService>();


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

// Build the service provider
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