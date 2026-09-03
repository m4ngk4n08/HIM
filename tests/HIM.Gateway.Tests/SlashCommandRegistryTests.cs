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

    [Theory]
    [InlineData("/help")]
    [InlineData("/menu")]
    [InlineData("/stats")]
    [InlineData("/matrix")]
    [InlineData("/game")]
    [InlineData("/theme")]
    [InlineData("/clear")]
    [InlineData("/exit")]
    public void Registry_RoutesEachCommandName_ToAResolvableHandler(string name)
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();

        Assert.True(registry.TryGet(name, out var command));
        Assert.NotNull(command);
    }
}
