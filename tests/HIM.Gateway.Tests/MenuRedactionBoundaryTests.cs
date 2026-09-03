using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH.CommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 21D (BL-8): pins the redaction boundary described on SanitizerExtension.RedactPhone -
/// PersonalInfo.Summary and a job's Highlights are redacted before /menu renders them, but a
/// short structured field (a project's Stack) deliberately is not. If that line ever moves, this
/// test fails and the move becomes a visible decision instead of a silent gap.
///
/// Uses phone-shaped canaries, never a real number - the exposed number is retired and purged
/// from history and must not reappear in any fixture.
/// </summary>
public class MenuRedactionBoundaryTests
{
    private const string RedactedSurfaceCanary = "555-010-2020";
    private const string StructuredFieldCanary = "555-010-3030";
    private const string RedactedMarker = "[REDACTED_PHONE]";

    private class ScriptedDispatcherHelper(params string[] responses) : ICommandDispatcherHelper
    {
        private readonly Queue<string> _responses = new(responses);

        public Task<string> ReadInputManualAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
            => Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : "exit");

        public Task SetScrollingRegionAsync(Stream stream, int top, int bottom, CancellationToken ct) => Task.CompletedTask;
        public Task ResetScrollingRegionAsync(Stream stream, CancellationToken ct) => Task.CompletedTask;
        public Task MoveCursorAsync(Stream stream, int row, int col, CancellationToken ct) => Task.CompletedTask;
    }

    private class NoOpTerminalLayoutService : ITerminalLayoutService
    {
        public Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct) => Task.CompletedTask;
    }

    private static PortfolioData BuildData() => new()
    {
        PersonalInfo = new PersonalInfo
        {
            Name = "Test Person",
            Role = "Engineer",
            Location = "Nowhere",
            Summary = $"Reach out at {RedactedSurfaceCanary} for more.",
            Contact = new Dictionary<string, string>()
        },
        Experiences =
        [
            new WorkExperience
            {
                Company = "Acme",
                Position = "Engineer",
                Duration = "2020-2021",
                Highlights = [$"Shipped a thing, call {RedactedSurfaceCanary} for details."]
            }
        ],
        Projects =
        [
            new ProjectItem { Name = "Widget", Stack = $"C# {StructuredFieldCanary}", Status = "Done" }
        ],
        TechnicalSkills = new Dictionary<string, List<string>>()
    };

    [Fact]
    public async Task Menu_RedactsSummaryAndHighlights_ButNotAShortStructuredField()
    {
        var service = new MenuCommandService(
            new ScriptedDispatcherHelper("1", "", "3", "", "4", "", "exit"),
            new NoOpTerminalLayoutService());

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });

        using var stream = new MemoryStream();
        await service.ExecuteAsync(console, stream, BuildData(), CancellationToken.None);

        var output = writer.ToString();

        // PersonalInfo.Summary and a job's Highlights go through RedactPhone before rendering.
        Assert.DoesNotContain(RedactedSurfaceCanary, output);
        Assert.Contains(RedactedMarker, output);

        // A project's Stack is short and structured, and deliberately not redacted - documents
        // the current boundary rather than pretending it is elsewhere.
        Assert.Contains(StructuredFieldCanary, output);
    }
}
