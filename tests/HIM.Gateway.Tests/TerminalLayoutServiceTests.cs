using System.Text.RegularExpressions;
using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Guards against the chrome renderer disagreeing with <see cref="ChromeLayoutPlanner"/>'s nominal
/// line counts, in either direction: a variant selected for a width its chrome text doesn't
/// actually fit in wraps, under-reports its row count, and lands the DECSTBM boundary inside the
/// chrome. Renders through the real <see cref="TerminalLayoutService"/> and counts rows from the
/// captured output (ANSI stripped) rather than trusting the nominal constants, so drift between the
/// planner and the renderer - in content, wrapping, or the constants themselves - shows up here.
/// </summary>
public class TerminalLayoutServiceTests
{
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    [Fact]
    public async Task CompactChromeLines_MatchesActualRenderedLineCount()
    {
        int measured = await RenderAndCountLines(width: 80, height: 24);
        var layout = ChromeLayoutPlanner.Decide(width: 80, height: 24);

        Assert.Equal(ChromeVariant.Compact, layout.Variant);
        Assert.Equal(ChromeLayoutPlanner.CompactChromeLines, measured);
    }

    /// <summary>
    /// Sweeps width at a height representative of each variant (24 keeps Full degraded to Compact
    /// at every width via invariant 1; 44 is the lowest height at which Full is actually selected)
    /// and asserts, for whichever variant <c>Decide</c> actually picks: the measured rendered row
    /// count equals the planner's nominal
    /// count, chrome never exceeds 30% of the height, and a set scrolling region always leaves at
    /// least 12 content rows - all checked against the real measured count, not the nominal one.
    /// </summary>
    [Theory]
    [InlineData(24)]
    [InlineData(44)]
    public async Task MeasuredChromeLines_MatchNominal_AcrossWidthSweep(int height)
    {
        var violations = new List<string>();

        for (int width = 40; width <= 200; width += 4)
        {
            var layout = ChromeLayoutPlanner.Decide(width, height);
            if (layout.Variant == ChromeVariant.None)
            {
                continue;
            }

            int measured = await RenderAndCountLines(width, height);

            if (measured != layout.ChromeLines)
            {
                violations.Add(
                    $"w={width} h={height} {layout.Variant}: nominal={layout.ChromeLines} measured={measured}");
            }

            if (measured > height * 0.30)
            {
                violations.Add(
                    $"w={width} h={height} {layout.Variant}: measured {measured} exceeds 30% of height ({height * 0.30}).");
            }

            if (layout.FirstScrollLine != 0)
            {
                int contentRows = height - measured;
                if (contentRows < 12)
                {
                    violations.Add(
                        $"w={width} h={height} {layout.Variant}: FirstScrollLine set but only {contentRows} measured content rows remain.");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    private static async Task<int> RenderAndCountLines(int width, int height)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = width;
        console.Profile.Height = height;

        var dispatcher = new RecordingCommandDispatcherHelper();
        var settings = Options.Create(new AiServiceSettings());
        var service = new TerminalLayoutService(dispatcher, new ThemeService(), settings);

        using var stream = new MemoryStream();
        await service.InitializeTerminalLayoutAsync(console, stream, CancellationToken.None);

        string plain = AnsiEscape.Replace(writer.ToString(), string.Empty);
        var lines = Regex.Split(plain, "\r\n|\n|\r");

        // Drop the single trailing empty element produced when the captured output ends in a
        // newline (every rendered row does) - not an intentional blank chrome row.
        int count = lines.Length;
        if (count > 0 && lines[^1].Length == 0)
        {
            count--;
        }

        return count;
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
