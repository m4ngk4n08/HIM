using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Commands;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// BL-7: HandleAiChatAsync used to only accumulate the streamed AI response into a buffer when
/// debug logging was enabled - with any logger above Debug (NullLogger included), every answer
/// rendered as "No response received." regardless of what the model actually said. This drives
/// the real CommandService end to end with a Debug-disabled logger and asserts the actual answer
/// reaches the console, proving the display path no longer depends on the logging level.
/// </summary>
public class AiChatCaptureTests
{
    private class NoOpCommandRegistry : ISlashCommandRegistry
    {
        public IReadOnlyList<SlashCommandDescriptor> Descriptors { get; } = Array.Empty<SlashCommandDescriptor>();

        public bool TryGet(string name, out ISlashCommand command)
        {
            command = null!;
            return false;
        }
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

    private class FakeAiClientService : IAiClientService
    {
        private readonly string[] _chunks;
        public FakeAiClientService(params string[] chunks) => _chunks = chunks;

        public IAsyncEnumerable<string> GetAiResponseAsync(string question, CancellationToken ct, string? correlationId = null)
            => Chunks(_chunks);

        public Task<(CitationResult? Result, string? Error)> GetCitationsAsync(string question, CancellationToken ct, string? correlationId = null)
            => Task.FromResult<(CitationResult?, string?)>((null, null));

        private static async IAsyncEnumerable<string> Chunks(string[] parts)
        {
            foreach (var p in parts)
            {
                yield return p;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task AiAnswer_ReachesTheConsole_EvenWithDebugLoggingDisabled()
    {
        var service = new CommandService(
            new FakeAiClientService("The capital of France ", "is Paris."),
            new NoOpCommandRegistry(),
            new NoOpDispatcherHelper(),
            new NoOpTerminalLayoutService(),
            new ThemeService(),
            NullLogger<CommandService>.Instance,
            new FixedPortfolioDataProvider(),
            new UserSessionState(),
            Options.Create(new SshSettings()));

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });

        using var stream = new MemoryStream();
        await service.ProcessCommandAsync(console, "What is the capital of France?", stream, CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("The capital of France is Paris.", output);
        Assert.DoesNotContain("No response received.", output);
    }
}
