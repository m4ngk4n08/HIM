using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Tests;

public class SlashCommandRegistryTests
{
    [Fact]
    public void Registry_ResolvesFromScopedProvider_WithAllEightCommandsDiscovered()
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();

        Assert.NotNull(registry);
        Assert.Equal(
            new[] { "/help", "/menu", "/stats", "/matrix", "/game", "/theme", "/clear", "/exit" },
            registry.Descriptors.OrderBy(d => d.HelpOrder).Select(d => d.Name));
    }

    /// <summary>
    /// Every discovered command name, taken from the catalog itself rather than typed out here.
    /// This is the point: the previous version of this test was eight [InlineData] lines, so a
    /// ninth command that nobody registered in DI was discovered, listed by /help, and still left
    /// every test in this file green - it only failed when a visitor typed it. Driving the cases
    /// from Descriptors means a new command is covered the moment it exists.
    /// </summary>
    public static TheoryData<string> EveryDiscoveredCommandName()
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();

        var data = new TheoryData<string>();
        foreach (var descriptor in registry.Descriptors)
            data.Add(descriptor.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryDiscoveredCommandName))]
    public void Registry_RoutesEachCommandName_ToAResolvableHandler(string name)
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();

        // TryGet resolves the handler out of the container, so this fails loudly if a discovered
        // command was never registered - the gap that made this test worth rewriting.
        Assert.True(registry.TryGet(name, out var command));
        Assert.NotNull(command);
    }
}
