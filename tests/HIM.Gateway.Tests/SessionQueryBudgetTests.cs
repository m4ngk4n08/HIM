using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 14E (SEC-04): a per-session AI query budget, enforced in the session scope
/// (UserSessionState - one instance per SSH connection). The AI service's own rate limiter
/// partitions by remote IP, but every request it sees arrives from the gateway container's one
/// address, so that limiter is effectively a single global bucket; this is the real per-identity
/// control. Once the budget is hit, the session must degrade to the static commands rather than
/// keep calling the AI service or erroring.
/// </summary>
public class SessionQueryBudgetTests
{
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

    private class NoOpDispatcherHelper : ICommandDispatcherHelper
    {
        public Task<string> ReadInputManualAsync(IAnsiConsole console, Stream stream, CancellationToken ct) => Task.FromResult(string.Empty);
        public Task SetScrollingRegionAsync(Stream stream, int top, int bottom, CancellationToken ct) => Task.CompletedTask;
        public Task ResetScrollingRegionAsync(Stream stream, CancellationToken ct) => Task.CompletedTask;
        public Task MoveCursorAsync(Stream stream, int row, int col, CancellationToken ct) => Task.CompletedTask;
    }

    private class NoOpTerminalLayoutService : ITerminalLayoutService
    {
        public Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct) => Task.CompletedTask;
    }

    private class FixedPortfolioDataProvider : IPortfolioDataProvider
    {
        public PortfolioData? Data => new();
    }

    private class NullLogger : ILogger<CommandService>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        private class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

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

    private static (CommandService Service, CountingAiClientService AiClient, StringWriter Writer) BuildService(int maxQueriesPerSession)
    {
        var aiClient = new CountingAiClientService();

        var service = new CommandService(
            aiClient,
            new NoOpGameService(),
            new NoOpMenuService(),
            new NoOpStatsService(),
            new NoOpMatrixService(),
            new NoOpDispatcherHelper(),
            new NoOpTerminalLayoutService(),
            new NullLogger(),
            new FixedPortfolioDataProvider(),
            new UserSessionState(),
            Options.Create(new SshSettings { MaxAiQueriesPerSession = maxQueriesPerSession }));

        var writer = new StringWriter();
        return (service, aiClient, writer);
    }

    private static IAnsiConsole ConsoleOver(StringWriter writer) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(writer)
    });

    [Fact]
    public async Task Queries_WithinBudget_AllReachTheAiClient()
    {
        var (service, aiClient, writer) = BuildService(maxQueriesPerSession: 3);
        using var stream = new MemoryStream();

        for (int i = 0; i < 3; i++)
        {
            await service.ProcessCommandAsync(ConsoleOver(writer), $"question {i}", stream, CancellationToken.None);
            // The 3-second inter-query cooldown would otherwise mask the budget check itself.
            await Task.Delay(3100);
        }

        Assert.Equal(3, aiClient.CallCount);
    }

    [Fact]
    public async Task QueryBeyondBudget_NeverReachesTheAiClient_AndPointsAtStaticCommands()
    {
        var (service, aiClient, writer) = BuildService(maxQueriesPerSession: 2);
        using var stream = new MemoryStream();

        for (int i = 0; i < 2; i++)
        {
            await service.ProcessCommandAsync(ConsoleOver(writer), $"question {i}", stream, CancellationToken.None);
            await Task.Delay(3100);
        }

        Assert.Equal(2, aiClient.CallCount);

        await service.ProcessCommandAsync(ConsoleOver(writer), "one more question", stream, CancellationToken.None);

        Assert.Equal(2, aiClient.CallCount); // unchanged - the third call never reached the AI client
        var output = writer.ToString();
        Assert.Contains("/menu", output);
        Assert.Contains("/stats", output);
    }
}
