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
    // first use. Exercised against an explicit, small set of fixture types (not the whole test
    // assembly - see the comment on SlashCommandCatalog.Discover(IEnumerable<Type>) for why:
    // this file also carries a duplicate-HelpOrder fixture pair for 19B, and a whole-assembly
    // scan can only ever surface whichever violation Type.GetTypes() happens to return first).
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
            () => SlashCommandCatalog.Discover(new[] { typeof(DuplicateFixtureA), typeof(DuplicateFixtureB) }));

        Assert.Contains("/dup", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Task 19B: HelpOrder collisions sorted nondeterministically (List<T>.Sort isn't stable), so
    // two commands sharing an order could make /help render two different tables between runs.
    // Chosen fix: throw at discovery, the same way a duplicate name already does.
    [SlashCommand("/order-a", "First", HelpOrder = 42)]
    private sealed class HelpOrderFixtureA : ISlashCommand
    {
        public Task ExecuteAsync(HIM.Gateway.Models.CommandContext context) => Task.CompletedTask;
    }

    [SlashCommand("/order-b", "Second - different name, same order", HelpOrder = 42)]
    private sealed class HelpOrderFixtureB : ISlashCommand
    {
        public Task ExecuteAsync(HIM.Gateway.Models.CommandContext context) => Task.CompletedTask;
    }

    [Fact]
    public void DuplicateHelpOrder_ThrowsAtDiscovery_NotAtFirstUse()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SlashCommandCatalog.Discover(new[] { typeof(HelpOrderFixtureA), typeof(HelpOrderFixtureB) }));

        Assert.Contains("42", ex.Message);
    }
}
