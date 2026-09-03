using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Tests;

public class SlashCommandRegistryTests
{
    [Fact]
    public void Registry_ResolvesFromScopedProvider_WithTheFourDelegatingCommandsDiscovered()
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();

        Assert.NotNull(registry);
        Assert.Equal(
            new[] { "/menu", "/stats", "/matrix", "/game" },
            registry.Descriptors.Select(d => d.Name));
        Assert.False(registry.TryGet("/help", out _));
    }

    [Theory]
    [InlineData("/menu")]
    [InlineData("/stats")]
    [InlineData("/matrix")]
    [InlineData("/game")]
    public void Registry_RoutesEachDelegatingCommandName_ToAResolvableHandler(string name)
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();

        Assert.True(registry.TryGet(name, out var command));
        Assert.NotNull(command);
    }
}
