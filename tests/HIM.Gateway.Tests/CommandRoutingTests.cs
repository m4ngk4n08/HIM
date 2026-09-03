using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// End-to-end routing through the real DI container: CommandService, the real
/// SlashCommandRegistry/SlashCommandCatalog, and the real HelpCommand/ClearCommand/
/// ExitCommand/ThemeCommand adapters. The four I*CommandService implementations they wrap
/// (menu/stats/matrix/game) are swapped for no-ops, same as SessionQueryBudgetTests and
/// InjectionRedactionSuiteTests - their own behavior isn't what's under test here, only whether
/// the registry reaches them.
/// </summary>
public class CommandRoutingTests
{
    private class CountingAiClientService : IAiClientService
    {
        public int CallCount { get; private set; }

        public IAsyncEnumerable<string> GetAiResponseAsync(string question, CancellationToken ct, string? correlationId = null)
        {
            CallCount++;
            return SingleChunk();

            async IAsyncEnumerable<string> SingleChunk()
            {
                yield return "An ordinary answer.";
                await Task.CompletedTask;
            }
        }
    }

    private class NoOpMenuService : IMenuCommandService
    {
        public Task ExecuteAsync(IAnsiConsole console, Stream stream, PortfolioData data, CancellationToken ct) => Task.CompletedTask;
    }

    private class NoOpStatsService : IStatsCommandService
    {
        public Task ExecuteAsync(IAnsiConsole console, Stream stream, PortfolioData data, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private class NoOpMatrixService : IMatrixCommandService
    {
        public Task ExecuteAsync(IAnsiConsole console, Stream stream, CancellationToken ct) => Task.CompletedTask;
    }

    private class NoOpGameService : IGameCommandService
    {
        public Task ExecuteAsync(IAnsiConsole console, Stream stream, CancellationToken ct) => Task.CompletedTask;
    }

    private class FixedPortfolioDataProvider : IPortfolioDataProvider
    {
        public PortfolioData? Data => new();
    }

    // The real TerminalLayoutService calls console.Clear(), which reaches into the actual
    // process console (not the redirected AnsiConsoleOutput) and throws outside a real
    // terminal. ClearCommand's own content isn't under test here - only that the registry
    // reaches it - so it's swapped out the same way the four game/menu/stats/matrix services are.
    private class NoOpTerminalLayoutService : ITerminalLayoutService
    {
        public Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct) => Task.CompletedTask;
    }

    private static (ServiceProvider Provider, CountingAiClientService AiClient) BuildProvider(int maxQueriesPerSession = 30)
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SshSettings:MaxAiQueriesPerSession"] = maxQueriesPerSession.ToString(),
                ["AiServiceSettings:ModelDisplayName"] = "test-model"
            })
            .Build();

        services.AddLogging();
        services.Configure<SshSettings>(configuration.GetSection("SshSettings"));
        services.Configure<AiServiceSettings>(configuration.GetSection("AiServiceSettings"));
        services.Configure<KnowledgeBaseSettings>(configuration.GetSection("KnowledgeBaseSettings"));

        services.AddService();

        var aiClient = new CountingAiClientService();
        services.AddSingleton<IAiClientService>(aiClient);
        services.AddSingleton<IPortfolioDataProvider, FixedPortfolioDataProvider>();
        services.AddScoped<ITerminalLayoutService, NoOpTerminalLayoutService>();
        services.AddScoped<IMenuCommandService, NoOpMenuService>();
        services.AddScoped<IStatsCommandService, NoOpStatsService>();
        services.AddScoped<IMatrixCommandService, NoOpMatrixService>();
        services.AddScoped<IGameCommandService, NoOpGameService>();

        var provider = services.BuildServiceProvider(ServiceExtensions.ContainerValidationOptions);
        return (provider, aiClient);
    }

    private static IAnsiConsole ConsoleOver(StringWriter writer) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(writer)
    });

    public static IEnumerable<object[]> AllCommandNames { get; } =
        new[] { "/help", "/menu", "/stats", "/matrix", "/game", "/theme", "/clear", "/exit" }
            .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(AllCommandNames))]
    public async Task RecognizedCommand_ReachesItsHandler_WithZeroAiCalls(string name)
    {
        using var provider = BuildProvider().Provider;
        using var scope = provider.CreateScope();
        var commandService = scope.ServiceProvider.GetRequiredService<ICommandService>();
        var aiClient = (CountingAiClientService)scope.ServiceProvider.GetRequiredService<IAiClientService>();
        var writer = new StringWriter();
        using var stream = new MemoryStream();

        try
        {
            await commandService.ProcessCommandAsync(ConsoleOver(writer), name, stream, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // /exit intentionally throws to end the session - not a failure here.
        }

        Assert.Equal(0, aiClient.CallCount);
    }

    [Fact]
    public async Task RecognizedCommand_DoesNotIncrementAiBudget_AndIsNeverRateLimited()
    {
        using var provider = BuildProvider().Provider;
        using var scope = provider.CreateScope();
        var commandService = scope.ServiceProvider.GetRequiredService<ICommandService>();
        var sessionState = scope.ServiceProvider.GetRequiredService<UserSessionState>();
        using var stream = new MemoryStream();

        // Two recognized commands back-to-back, no delay - if either were treated as an AI
        // query, the second would trip the 3-second cooldown.
        var firstWriter = new StringWriter();
        await commandService.ProcessCommandAsync(ConsoleOver(firstWriter), "/menu", stream, CancellationToken.None);
        var secondWriter = new StringWriter();
        await commandService.ProcessCommandAsync(ConsoleOver(secondWriter), "/stats", stream, CancellationToken.None);

        Assert.Equal(0, sessionState.AiQueryCount);
        Assert.DoesNotContain("cooling down", secondWriter.ToString());
    }

    [Fact]
    public async Task UnrecognizedCommand_ReachesTheAiClient_AndCountsAgainstTheBudget()
    {
        using var provider = BuildProvider().Provider;
        using var scope = provider.CreateScope();
        var commandService = scope.ServiceProvider.GetRequiredService<ICommandService>();
        var aiClient = (CountingAiClientService)scope.ServiceProvider.GetRequiredService<IAiClientService>();
        var sessionState = scope.ServiceProvider.GetRequiredService<UserSessionState>();
        var writer = new StringWriter();
        using var stream = new MemoryStream();

        await commandService.ProcessCommandAsync(ConsoleOver(writer), "What does Angelo build?", stream, CancellationToken.None);

        Assert.Equal(1, aiClient.CallCount);
        Assert.Equal(1, sessionState.AiQueryCount);
    }

    [Fact]
    public async Task UppercaseCommandName_StillRoutes_CaseInsensitively()
    {
        using var provider = BuildProvider().Provider;
        using var scope = provider.CreateScope();
        var commandService = scope.ServiceProvider.GetRequiredService<ICommandService>();
        var aiClient = (CountingAiClientService)scope.ServiceProvider.GetRequiredService<IAiClientService>();
        var writer = new StringWriter();
        using var stream = new MemoryStream();

        await commandService.ProcessCommandAsync(ConsoleOver(writer), "/HELP", stream, CancellationToken.None);

        Assert.Equal(0, aiClient.CallCount);
        Assert.Contains("COMMANDS", writer.ToString());
    }

    [Fact]
    public async Task LeadingSpaceBeforeCommand_GoesToTheAi_NotToTheHandler()
    {
        // Pins the tokenizing rule against a future "cleanup" adding RemoveEmptyEntries:
        // Split(' ')[0] on " /help" yields "", which matches nothing.
        using var provider = BuildProvider().Provider;
        using var scope = provider.CreateScope();
        var commandService = scope.ServiceProvider.GetRequiredService<ICommandService>();
        var aiClient = (CountingAiClientService)scope.ServiceProvider.GetRequiredService<IAiClientService>();
        var writer = new StringWriter();
        using var stream = new MemoryStream();

        await commandService.ProcessCommandAsync(ConsoleOver(writer), " /help", stream, CancellationToken.None);

        Assert.Equal(1, aiClient.CallCount);
        Assert.DoesNotContain("COMMANDS", writer.ToString());
    }
}
