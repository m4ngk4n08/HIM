using System.Reflection;
using HIM.Gateway.Extensions;
using HIM.Gateway.Services.SSH.Commands;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 18's reason for existing: today a handler could be added as a case in
/// CommandService's switch without touching /help's hardcoded table, or vice versa, and nothing
/// would catch it. These tests pin the two invariants that make that drift structurally
/// impossible instead of a matter of remembering to update both places.
/// </summary>
public class SlashCommandDiscoveryTests
{
    private static Assembly GatewayAssembly => typeof(ServiceExtensions).Assembly;

    [Fact]
    public void EveryAttributedType_ImplementsISlashCommand_AndEveryImplementation_CarriesTheAttribute()
    {
        // Catches exactly the gap the switch had: a handler added later without wiring it up
        // (no attribute -> invisible to the catalog -> never routed, never in /help) or a class
        // hand-attributed without implementing the interface (would fail to route at all).
        var attributedTypes = GatewayAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<SlashCommandAttribute>() is not null)
            .OrderBy(t => t.FullName)
            .ToList();

        var implementingTypes = GatewayAssembly.GetTypes()
            .Where(t => typeof(ISlashCommand).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .OrderBy(t => t.FullName)
            .ToList();

        Assert.NotEmpty(attributedTypes);
        Assert.Equal(implementingTypes, attributedTypes);
    }

    [Fact]
    public void NoTwoDiscoveredCommands_ShareAName_CaseInsensitively()
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();

        var distinctNames = registry.Descriptors
            .Select(d => d.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(registry.Descriptors.Count, distinctNames.Count());
    }

    // Authorized behavior change #3: a duplicate command name now fails at startup, not at
    // first use. Exercised against a small synthetic assembly (this test assembly, holding
    // exactly these two fixtures) rather than the real Gateway assembly, so the failure is
    // pinned to the duplicate itself.
    [SlashCommand("/dup", "First")]
    private sealed class DuplicateFixtureA : ISlashCommand
    {
        public Task ExecuteAsync(HIM.Gateway.Models.CommandContext context) => Task.CompletedTask;
    }

    [SlashCommand("/DUP", "Second - same name, different case")]
    private sealed class DuplicateFixtureB : ISlashCommand
    {
        public Task ExecuteAsync(HIM.Gateway.Models.CommandContext context) => Task.CompletedTask;
    }

    [Fact]
    public void DuplicateCommandName_ThrowsAtDiscovery_NotAtFirstUse()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SlashCommandCatalog.Discover(typeof(DuplicateFixtureA).Assembly));

        Assert.Contains("/dup", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
