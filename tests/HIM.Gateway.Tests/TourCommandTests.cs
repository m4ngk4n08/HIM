using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.CommandDispatcher;
using HIM.Gateway.Services.SSH.Commands;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Linq;
using System.Text;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 26D: /tour's navigation grammar (next/back/exit/a step number), driven against the real
/// TourCommand + CommandDispatcherHelper + SessionByteReader pair, the same seam
/// InputBufferRaceTests exercises for /menu. The idle-timeout test below additionally drives the
/// real ConsoleEngineService, proving a visitor reading a tour step is never disconnected for
/// inactivity - the same guarantee Task 25C pinned for /menu's nested reads.
/// </summary>
public class TourCommandTests
{
    private const string Canary = "555-010-2020";

    private static PortfolioData BuildData(string? phoneInContact = null) => new()
    {
        PersonalInfo = new PersonalInfo
        {
            Name = "Angelo",
            Role = "Software Engineer",
            Location = "Remote",
            Summary = "Builds things.",
            Contact = phoneInContact is null
                ? new Dictionary<string, string> { ["github"] = "angelodavales" }
                : new Dictionary<string, string> { ["phone"] = phoneInContact }
        },
        Experiences = [new WorkExperience { Company = "Acme", Position = "Engineer", Duration = "2020-2024", Highlights = ["Shipped things."] }],
        TechnicalSkills = new Dictionary<string, List<string>> { ["backend"] = ["C#", ".NET"] },
        Projects = [new ProjectItem { Name = "HIM", Stack = ".NET 10", Status = "Live" }]
    };

    private static IAnsiConsole BuildConsole(TextWriter writer) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(writer)
    });

    private static (TourCommand Command, TourState State, StringWriter Output) BuildCommand()
    {
        var byteReader = new SessionByteReader();
        var dispatcher = new CommandDispatcherHelper(byteReader);
        var state = new TourState();
        var command = new TourCommand(dispatcher, state, new ThemeService());
        return (command, state, new StringWriter());
    }

    private static async Task<string> RunAsync(
        TourCommand command, TourState state, StringWriter writer, string rawCommand, string scriptedInput, PortfolioData? data = null)
    {
        var console = BuildConsole(writer);
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(scriptedInput));
        var context = new CommandContext(console, stream, rawCommand, data ?? BuildData(), "session", CancellationToken.None);

        await command.ExecuteAsync(context);
        return writer.ToString();
    }

    [Fact]
    public async Task QuickMode_WalksAllFiveSteps_InOrder_ThenEndsCleanly()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour", "\n\n\n\n\n");

        var titles = new[] { "WELCOME", "SKILLS & STACK", "EXPERIENCE", "PROJECTS", "WRAP-UP" };
        var positions = titles.Select(t => output.IndexOf(t, StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);

        Assert.Contains("Tour ended.", output);
        Assert.False(state.IsActive);
    }

    [Fact]
    public async Task RecruiterMode_RendersItsOwnFourSteps()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour recruiter", "\n\n\n\n");

        Assert.Contains("EXPERIENCE", output);
        Assert.Contains("SKILLS & STACK", output);
        Assert.Contains("PROJECTS", output);
        Assert.Contains("CONTACT", output);
        Assert.DoesNotContain("WRAP-UP", output);
        Assert.Equal(TourMode.Recruiter, state.Mode);
    }

    [Fact]
    public async Task EngineerMode_RendersItsOwnFiveSteps()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour engineer", "\n\n\n\n\n");

        Assert.Contains("ARCHITECTURE", output);
        Assert.Contains("THE AI / RAG PIPELINE", output);
        Assert.Equal(TourMode.Engineer, state.Mode);
    }

    [Fact]
    public async Task UnknownMode_FallsBackToQuick_WithUsageHint()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour bogus", "exit\n");

        Assert.Contains("Unknown tour mode", output);
        Assert.Contains("/tour", output);
        Assert.Contains("WELCOME", output);
        Assert.Equal(TourMode.Quick, state.Mode);
    }

    [Fact]
    public async Task BackOnFirstStep_IsANoOp()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour", "back\nexit\n");

        // WELCOME is rendered twice: once on entry, once again after "back" was a no-op and the
        // loop re-rendered the same (still first) step.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(output, "WELCOME").Count);
    }

    [Fact]
    public async Task NumericJumpOutOfRange_IsRejected_WithoutEndingTheTour()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour", "99\nexit\n");

        Assert.Contains("Didn't catch that", output);
        // Only ended because of the explicit "exit" on the second line, not the bad jump.
        Assert.Contains("Tour ended.", output);
    }

    [Fact]
    public async Task NumericJump_ValidStepNumber_JumpsThere()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour", "4\nexit\n");

        Assert.Contains("PROJECTS", output);
    }

    [Fact]
    public async Task ExitCommand_EndsTheTour_AndLeavesNoLeftoverState()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour", "exit\n");

        Assert.False(state.IsActive);
        // TourCommand never writes its own prompt - ConsoleEngineService's outer loop owns "> ".
        Assert.DoesNotContain("> ", output);
    }

    [Fact]
    public async Task QAlias_AlsoEndsTheTour()
    {
        var (command, state, writer) = BuildCommand();

        var output = await RunAsync(command, state, writer, "/tour", "q\n");

        Assert.False(state.IsActive);
        // "q" must end the tour on the first step, not fall through to being an unrecognized
        // command that leaves the tour running until it walks off the end naturally.
        Assert.DoesNotContain("WRAP-UP", output);
        Assert.DoesNotContain("Didn't catch that", output);
    }

    [Fact]
    public async Task PhoneNumberInContact_NeverRendersUnredacted_ThroughTheRealCommand()
    {
        var (command, state, writer) = BuildCommand();
        var data = BuildData(phoneInContact: Canary);

        var output = await RunAsync(command, state, writer, "/tour recruiter", "\n\n\n\n", data);

        Assert.DoesNotContain(Canary, output);
        Assert.Contains("[REDACTED_PHONE]", output);
    }

    [Fact]
    public async Task TwoConcurrentSessions_HoldIndependentTourPositions()
    {
        var (commandOne, stateOne, writerOne) = BuildCommand();
        var (commandTwo, stateTwo, writerTwo) = BuildCommand();

        // Session one jumps to the last step and exits there; session two only takes one "next".
        var taskOne = RunAsync(commandOne, stateOne, writerOne, "/tour", "5\nexit\n");
        var taskTwo = RunAsync(commandTwo, stateTwo, writerTwo, "/tour", "exit\n");
        await Task.WhenAll(taskOne, taskTwo);

        Assert.False(stateOne.IsActive);
        Assert.False(stateTwo.IsActive);
        // Each TourCommand actually used the TourState instance it was constructed with, not a
        // shared one - session one's jump to step 5 (index 4) must land on stateOne, and must
        // never be visible on stateTwo, which only ever took a single "exit".
        Assert.Equal(4, stateOne.CurrentStepIndex);
        Assert.Equal(0, stateTwo.CurrentStepIndex);
        Assert.NotSame(stateOne, stateTwo);
        Assert.Contains("WRAP-UP", await taskOne);
        Assert.DoesNotContain("WRAP-UP", await taskTwo);
    }

    // --- Idle timeout: the real ConsoleEngineService, not a direct TourCommand call ---

    private sealed class NoOpTerminalLayoutService : ITerminalLayoutService
    {
        public Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Dispatches "/tour" to a real TourCommand, exactly as CommandService does, so the
    /// idle-timeout test below exercises the real navigation loop under the real per-iteration
    /// timeout CTS, not a stand-in.</summary>
    private sealed class TourDispatchingCommandService : ICommandService
    {
        private readonly TourCommand _tourCommand;
        private readonly PortfolioData _data;

        public TourDispatchingCommandService(TourCommand tourCommand, PortfolioData data)
        {
            _tourCommand = tourCommand;
            _data = data;
        }

        public async Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct)
        {
            if (command.StartsWith("/tour", StringComparison.Ordinal))
            {
                var context = new CommandContext(console, stream, command, _data, "session", ct);
                await _tourCommand.ExecuteAsync(context);
            }
        }
    }

    /// <summary>Same scripted-stream shape as InputBufferRaceTests.ScriptedStream - a fixed
    /// (delay, bytes) script, one real ReadAsync call standing in for each step, blocking on any
    /// further read once exhausted rather than returning EOF.</summary>
    private sealed class ScriptedStream : Stream
    {
        private readonly List<(TimeSpan Delay, byte[] Bytes)> _script;
        private int _stepIndex;
        private int _byteOffset;
        private bool _delayApplied;
        private readonly TaskCompletionSource<int> _blockedRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScriptedStream(params (TimeSpan Delay, string Text)[] steps) =>
            _script = steps.Select(s => (s.Delay, Encoding.ASCII.GetBytes(s.Text))).ToList();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_stepIndex < _script.Count)
            {
                var (delay, bytes) = _script[_stepIndex];
                if (!_delayApplied)
                {
                    _delayApplied = true;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
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
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task VisitorReadingATourStep_IsNotDisconnectedByTheIdleTimer()
    {
        // IdleTimeoutSeconds=1, but the answer to /tour's first step arrives 1.3s later - if the
        // outer loop's per-iteration idle CTS wrapped the nested tour read (the mistake Task 25C
        // guards against for /menu), this would time out before the answer ever arrived.
        var byteReader = new SessionByteReader();
        var dispatcher = new CommandDispatcherHelper(byteReader);
        var state = new TourState();
        var tourCommand = new TourCommand(dispatcher, state, new ThemeService());
        var commandService = new TourDispatchingCommandService(tourCommand, BuildData());

        var engine = new ConsoleEngineService(
            commandService,
            new NoOpTerminalLayoutService(),
            new ThemeService(),
            byteReader,
            Options.Create(new SshSettings { IdleTimeoutSeconds = 1 }));

        var output = new StringWriter();
        var console = BuildConsole(output);

        using var stream = new ScriptedStream(
            (TimeSpan.Zero, "/tour\r"),
            (TimeSpan.FromMilliseconds(1300), "next\rexit\r"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        try
        {
            await engine.HandleInteractionLoopAsync(console, stream, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // The nested tour read must succeed (and the tour must reach its exit) before anything
        // idle-related fires. Once /tour itself is done, the outer loop's own idle timer taking
        // over and eventually closing the now-inactive session is expected, correct behavior -
        // not what this test is pinning - so this checks ordering, not absence.
        var rendered = output.ToString();
        var skillsIndex = rendered.IndexOf("SKILLS & STACK", StringComparison.Ordinal);
        var endedIndex = rendered.IndexOf("Tour ended.", StringComparison.Ordinal);
        var timedOutIndex = rendered.IndexOf("timed out", StringComparison.OrdinalIgnoreCase);

        Assert.True(skillsIndex >= 0);
        Assert.True(endedIndex >= 0);
        Assert.True(timedOutIndex < 0 || timedOutIndex > endedIndex);
    }
}
