using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.CommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Linq;
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
    /// records what it got back, the same way MenuCommandService really does. Any other command
    /// is just recorded, for the idle-timeout tests below.</summary>
    private sealed class NestedPromptCapturingCommandService : ICommandService
    {
        private readonly ICommandDispatcherHelper _dispatcher;
        public string? CapturedNestedInput { get; private set; }
        public List<string> ProcessedCommands { get; } = new();

        public NestedPromptCapturingCommandService(ICommandDispatcherHelper dispatcher) => _dispatcher = dispatcher;

        public async Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct)
        {
            ProcessedCommands.Add(command);
            if (command == "/menu")
            {
                CapturedNestedInput = await _dispatcher.ReadInputManualAsync(console, stream, ct);
            }
        }
    }

    /// <summary>
    /// Serves a fixed script of (delay, bytes) reads in order - each one simulating a single real
    /// ReadAsync call from a client, optionally with a real-time gap beforehand to stand in for a
    /// visitor thinking or a network round trip (ConsoleEngineService's idle timer has no
    /// TimeProvider seam of its own to fake instead - see the class comment on CancelAfter). Once
    /// the script is exhausted it blocks on any further read (never returning 0/EOF) until the
    /// caller's token is cancelled, like a client that has gone quiet.
    /// </summary>
    private sealed class ScriptedStream : Stream
    {
        // A List, not a Queue: the nested reader asks for one byte at a time, so a step's bytes
        // may need to be handed out across several ReadAsync calls - only the delay before a step
        // fires once, on its first (possibly partial) read.
        private readonly List<(TimeSpan Delay, byte[] Bytes)> _script;
        private int _stepIndex;
        private int _byteOffset;
        private bool _delayApplied;
        private readonly TaskCompletionSource<int> _blockedRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScriptedStream(params (TimeSpan Delay, string Text)[] steps)
        {
            _script = steps.Select(s => (s.Delay, Encoding.ASCII.GetBytes(s.Text))).ToList();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_stepIndex < _script.Count)
            {
                var (delay, bytes) = _script[_stepIndex];
                if (!_delayApplied)
                {
                    _delayApplied = true;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, ct);
                    }
                }

                int n = Math.Min(count, bytes.Length - _byteOffset);
                Array.Copy(bytes, _byteOffset, buffer, offset, n);
                _byteOffset += n;

                if (_byteOffset >= bytes.Length)
                {
                    _stepIndex++;
                    _byteOffset = 0;
                    _delayApplied = false;
                }

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

    private static (ConsoleEngineService Engine, NestedPromptCapturingCommandService Commands) BuildEngine(int idleTimeoutSeconds)
    {
        var byteReader = new SessionByteReader();
        var dispatcher = new CommandDispatcherHelper(byteReader);
        var fakeCommandService = new NestedPromptCapturingCommandService(dispatcher);

        var engine = new ConsoleEngineService(
            fakeCommandService,
            new NoOpTerminalLayoutService(),
            new ThemeService(),
            byteReader,
            Options.Create(new SshSettings { IdleTimeoutSeconds = idleTimeoutSeconds }));

        return (engine, fakeCommandService);
    }

    private static IAnsiConsole BuildConsole(TextWriter writer) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(writer)
    });

    [Fact]
    public async Task MultiLineChunk_NestedMenuPrompt_ReceivesThePastedSelection()
    {
        var (engine, commands) = BuildEngine(idleTimeoutSeconds: 300);
        var console = BuildConsole(new StringWriter());

        // "/menu" then Enter, then "1" then Enter - both lines arrive in one ReadAsync call,
        // as a real client's paste or a burst of queued keystrokes can deliver them.
        using var stream = new ScriptedStream((TimeSpan.Zero, "/menu\r1\r"));
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

        Assert.Equal("1", commands.CapturedNestedInput);
        // "1" belongs to the nested prompt only - it must never also get dispatched as its own
        // top-level command (which is what happens if the pushed-back bytes get walked a second
        // time instead of handed off exactly once).
        Assert.Equal(new[] { "/menu" }, commands.ProcessedCommands);
    }

    [Fact]
    public async Task WindowsStyleLineEndings_NestedMenuPrompt_StillReceivesJustTheSelection()
    {
        var (engine, commands) = BuildEngine(idleTimeoutSeconds: 300);
        var console = BuildConsole(new StringWriter());

        // A clipboard paste is the realistic trigger the brief calls out, and pasted text commonly
        // carries "\r\n" line endings. The pushed-back LF that follows "/menu"'s CR must not be
        // replayed to the nested reader as a second, empty Enter before it ever sees "1".
        using var stream = new ScriptedStream((TimeSpan.Zero, "/menu\r\n1\r\n"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await engine.HandleInteractionLoopAsync(console, stream, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal("1", commands.CapturedNestedInput);
        Assert.Equal(new[] { "/menu" }, commands.ProcessedCommands);
    }

    [Fact]
    public async Task NestedPromptAnsweredSlowly_DoesNotTriggerTheIdleTimeout()
    {
        // 25C: the idle timer must not start counting down against a nested prompt the visitor
        // is actively answering. IdleTimeoutSeconds=1, but the answer to /menu arrives 1.3s later -
        // if the outer loop's per-iteration idle CTS wrapped the nested read, this would time out
        // and the answer would never be captured.
        var (engine, commands) = BuildEngine(idleTimeoutSeconds: 1);
        var output = new StringWriter();
        var console = BuildConsole(output);

        using var stream = new ScriptedStream(
            (TimeSpan.Zero, "/menu\r"),
            (TimeSpan.FromMilliseconds(1300), "1\r"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await engine.HandleInteractionLoopAsync(console, stream, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal("1", commands.CapturedNestedInput);
        Assert.DoesNotContain("timed out", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealInputAcrossIterations_KeepsResettingTheIdleTimeout()
    {
        // 25C's other half: the idle timer must still reset on real visitor input. Three ordinary
        // commands, each arriving 700ms after the last (under the 1s per-iteration budget), total
        // 1.4s - longer than a single un-reset 1s deadline from session start would allow. If the
        // timer only reset by luck (or not at all), the third command would never arrive.
        var (engine, commands) = BuildEngine(idleTimeoutSeconds: 1);
        var output = new StringWriter();
        var console = BuildConsole(output);

        using var stream = new ScriptedStream(
            (TimeSpan.Zero, "hi\r"),
            (TimeSpan.FromMilliseconds(700), "bye\r"),
            (TimeSpan.FromMilliseconds(700), "yo\r"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await engine.HandleInteractionLoopAsync(console, stream, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(new[] { "hi", "bye", "yo" }, commands.ProcessedCommands);
        Assert.DoesNotContain("timed out", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
