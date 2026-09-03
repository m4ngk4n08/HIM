using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Tests;

public class SlashCommandRegistryTests
{
    [Fact]
    public void Registry_ResolvesFromScopedProvider_BeforeAnyHandlerIsRegistered()
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();

        Assert.NotNull(registry);
        Assert.Empty(registry.Descriptors);
        Assert.False(registry.TryGet("/help", out _));
    }
}
