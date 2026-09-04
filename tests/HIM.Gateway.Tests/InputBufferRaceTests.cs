using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.CommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Text;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 25A: reproduces the input-buffer race between ConsoleEngineService's outer read loop
/// and CommandDispatcherHelper's nested one-byte-at-a-time prompt reader, against the real seam
/// (a real ConsoleEngineService driving a real CommandDispatcherHelper over one Stream) rather
/// than a description of the bug.
/// </summary>
public class InputBufferRaceTests
{
    private sealed class NoOpTerminalLayoutService : ITerminalLayoutService
    {
        public Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Stands in for CommandService: on "/menu" it drives the real nested reader and
    /// records what it got back, the same way MenuCommandService really does.</summary>
    private sealed class NestedPromptCapturingCommandService : ICommandService
    {
        private readonly ICommandDispatcherHelper _dispatcher;
        public string? CapturedNestedInput { get; private set; }

        public NestedPromptCapturingCommandService(ICommandDispatcherHelper dispatcher) => _dispatcher = dispatcher;

        public async Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct)
        {
            if (command == "/menu")
            {
                CapturedNestedInput = await _dispatcher.ReadInputManualAsync(console, stream, ct);
            }
        }
    }

    /// <summary>
    /// Hands back its whole payload in a single ReadAsync call - simulating a client that pastes
    /// a multi-line command plus its answer in one burst - then blocks on any further read
    /// (never returning 0/EOF, which a nested reader could mistake for "line already there") until
    /// the caller's token is cancelled.
    /// </summary>
    private sealed class SingleChunkThenBlockStream : Stream
    {
        private readonly byte[] _chunk;
        private bool _served;
        private readonly TaskCompletionSource<int> _blockedRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SingleChunkThenBlockStream(string text) => _chunk = Encoding.ASCII.GetBytes(text);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (!_served)
            {
                _served = true;
                int n = Math.Min(count, _chunk.Length);
                Array.Copy(_chunk, 0, buffer, offset, n);
                return n;
            }

            await using (ct.Register(() => _blockedRead.TrySetCanceled(ct)).ConfigureAwait(false))
            {
                return await _blockedRead.Task;
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task MultiLineChunk_NestedMenuPrompt_ReceivesThePastedSelection()
    {
        var dispatcher = new CommandDispatcherHelper();
        var fakeCommandService = new NestedPromptCapturingCommandService(dispatcher);

        var engine = new ConsoleEngineService(
            fakeCommandService,
            new NoOpTerminalLayoutService(),
            new ThemeService(),
            Options.Create(new SshSettings { IdleTimeoutSeconds = 300 }));

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });

        // "/menu" then Enter, then "1" then Enter - both lines arrive in one ReadAsync call,
        // as a real client's paste or a burst of queued keystrokes can deliver them.
        using var stream = new SingleChunkThenBlockStream("/menu\r1\r");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await engine.HandleInteractionLoopAsync(console, stream, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected once nothing more will ever arrive on the stream and the test's own
            // timeout fires - the assertion below is what actually matters.
        }

        Assert.Equal("1", fakeCommandService.CapturedNestedInput);
    }
}
