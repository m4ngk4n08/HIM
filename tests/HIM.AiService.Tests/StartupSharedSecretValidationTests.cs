using HIM.AiService.Models.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HIM.AiService.Tests;

/// <summary>
/// Exercises the same AddOptions().Validate().ValidateOnStart() pipeline used in
/// HIM.AiService's Program.cs, in isolation from the full web host (which would otherwise
/// pull in ONNX/Groq/Gemini configuration just to boot).
/// </summary>
public class StartupSharedSecretValidationTests
{
    private static IHost BuildHost(string? sharedSecret)
    {
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiSettings:Security:SharedSecret"] = sharedSecret
            });
        });
        builder.ConfigureServices((ctx, services) =>
        {
            services.AddOptions<AiSettings>()
                .Bind(ctx.Configuration.GetSection(nameof(AiSettings)))
                .Validate(
                    s => !string.IsNullOrWhiteSpace(s.Security.SharedSecret),
                    "AiSettings:Security:SharedSecret must be configured. Refusing to start without a shared secret.")
                .ValidateOnStart();
        });
        return builder.Build();
    }

    [Fact]
    public async Task StartAsync_Throws_WhenSharedSecretNotConfigured()
    {
        using var host = BuildHost(sharedSecret: null);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task StartAsync_Throws_WhenSharedSecretIsWhitespace()
    {
        using var host = BuildHost(sharedSecret: "   ");

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task StartAsync_Succeeds_WhenSharedSecretConfigured()
    {
        using var host = BuildHost(sharedSecret: "a-real-secret");

        await host.StartAsync();
        await host.StopAsync();
    }
}
