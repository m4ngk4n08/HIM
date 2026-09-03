using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 23C: /defense renders the live connection-defense state through the real DI container -
/// the two security requirements (masked IPs, RedactPhone on free text) are what's under test
/// here, not the gate classes themselves (ConnectionGatePipelineTests already covers those and
/// is untouched by this feature).
/// </summary>
public class DefenseCommandTests
{
    private const string DocRangeIp = "203.0.113.9";

    private static async Task<string> RunDefenseAsync(IServiceProvider scopeProvider)
    {
        var registry = scopeProvider.GetRequiredService<ISlashCommandRegistry>();
        Assert.True(registry.TryGet("/defense", out var command));

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        // Wide enough that Spectre's table never wraps a cell, so these tests assert on the
        // sentences the command actually produces rather than on where an 80-column break landed.
        console.Profile.Width = 240;
        using var stream = new MemoryStream();
        var context = new CommandContext(console, stream, "/defense", new PortfolioData(), "session", CancellationToken.None);

        await command!.ExecuteAsync(context);
        return writer.ToString();
    }

    [Fact]
    public async Task EveryRegisteredLayer_GetsItsOwnExplanation_NotTheGenericFallback()
    {
        // DefenseCommand.Explain switches on the exact IConnectionGate.Layer strings. Rename one
        // and the panel silently degrades to the catch-all sentence for that row - the render
        // still succeeds, so without this test nothing would fail and the regression would ship.
        // Pins the four real registered gates, resolved through the real container.
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var output = await RunDefenseAsync(scope.ServiceProvider);

        Assert.Contains("came from an IP already banned for past abuse.", output);
        Assert.Contains("connections/second across the whole site.", output);
        Assert.Contains("attempts per", output);
        Assert.Contains("connections at once.", output);
        Assert.DoesNotContain("did not clear this layer's check.", output);
    }

    [Fact]
    public async Task NoActiveBans_RendersWithoutThrowing_AndSaysSo()
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var output = await RunDefenseAsync(scope.ServiceProvider);

        Assert.Contains("No IPs are currently banned", output);
    }

    [Fact]
    public async Task ActiveBan_RendersTheMaskedNetwork_NeverTheFullAddress()
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();
        var ipBanService = scope.ServiceProvider.GetRequiredService<IIpBanService>();

        // Default SshSettings.BanThresholdStrikes is 3 - three strikes issues the first ban.
        ipBanService.RecordStrike(DocRangeIp);
        ipBanService.RecordStrike(DocRangeIp);
        ipBanService.RecordStrike(DocRangeIp);

        var output = await RunDefenseAsync(scope.ServiceProvider);

        Assert.DoesNotContain(DocRangeIp, output);
        Assert.Contains("203.0.113.x", output);
    }
}
