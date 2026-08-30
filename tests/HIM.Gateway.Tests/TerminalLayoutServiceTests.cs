using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Guards against <see cref="ChromeLayoutPlanner.CompactChromeLines"/> drifting away from what
/// <see cref="TerminalLayoutService"/> actually renders for <see cref="ChromeVariant.Compact"/>.
/// The two are independent by construction - the planner constant is a nominal figure used only
/// to pick and validate the variant, while the renderer measures its own real output for the
/// DECSTBM boundary - so nothing else catches them disagreeing.
/// </summary>
public class TerminalLayoutServiceTests
{
    [Fact]
    public async Task CompactChromeLines_MatchesActualRenderedLineCount()
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;

        var dispatcher = new RecordingCommandDispatcherHelper();
        var settings = Options.Create(new AiServiceSettings());
        var service = new TerminalLayoutService(dispatcher, settings);

        using var stream = new MemoryStream();
        var layout = ChromeLayoutPlanner.Decide(console.Profile.Width, console.Profile.Height);
        Assert.Equal(ChromeVariant.Compact, layout.Variant);

        await service.InitializeTerminalLayoutAsync(console, stream, CancellationToken.None);

        Assert.NotNull(dispatcher.ScrollingRegionTop);
        int actualRenderedLines = dispatcher.ScrollingRegionTop!.Value - 1;

        Assert.Equal(ChromeLayoutPlanner.CompactChromeLines, actualRenderedLines);
    }

    private sealed class RecordingCommandDispatcherHelper : ICommandDispatcherHelper
    {
        public int? ScrollingRegionTop { get; private set; }

        public Task<string> ReadInputManualAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
            => Task.FromResult(string.Empty);

        public Task SetScrollingRegionAsync(Stream stream, int top, int bottom, CancellationToken ct)
        {
            ScrollingRegionTop = top;
            return Task.CompletedTask;
        }

        public Task ResetScrollingRegionAsync(Stream stream, CancellationToken ct) => Task.CompletedTask;

        public Task MoveCursorAsync(Stream stream, int row, int col, CancellationToken ct) => Task.CompletedTask;
    }
}
