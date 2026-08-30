using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;

namespace HIM.Gateway.Services.SSH;

public class TerminalLayoutService : ITerminalLayoutService
{
    private readonly ICommandDispatcherHelper _commandDispatcher;
    private readonly string _modelDisplayName;
    private readonly string[] _funFacts = new[]
    {
        "🚀 Running on a $4/month VPS",
        "🧠 Powered by ONNX + Groq LPU",
        "⚡ SIMD‑accelerated vector search",
        "🔒 Hardened with nftables + Fail2Ban",
        "💻 Built with .NET 10 & C#",
        "🎮 TUI Game Engine built-in",
        "🌐 80% cost reduction vs Python RAG",
        "📦 Native AOT ready (Project Loom)",
        "🧬 All‑in‑process embedding pipeline",
        "🔐 Zero external API calls for embeddings"
    };
    private int _funFactIndex = 0;

    public TerminalLayoutService(ICommandDispatcherHelper commandDispatcher, IOptions<AiServiceSettings> aiServiceSettings)
    {
        _commandDispatcher = commandDispatcher;
        _modelDisplayName = aiServiceSettings.Value.ModelDisplayName;
    }

    public async Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
    {
        // 1. Reset terminal and clear
        await stream.WriteAsync(Encoding.UTF8.GetBytes("\x1b[2J\x1b[H"), ct);
        console.Clear();

        // 2. Decide what to draw for this terminal size. This is the pure decision (see
        // ChromeLayout.cs) - width/height in, variant + invariants out, no rendering yet.
        var layout = ChromeLayoutPlanner.Decide(console.Profile.Width, console.Profile.Height);

        // 3. Render the chosen variant and measure the *actual* rendered output as we go,
        // rather than guessing. int lineCount tracks the true row count so the DECSTBM
        // boundary below reflects what was really drawn, not an estimate of it.
        int lineCount = 0;

        switch (layout.Variant)
        {
            case ChromeVariant.Full:
                lineCount += RenderHeader(console);
                console.WriteLine();
                lineCount++;

                RenderStatusBar(console);
                lineCount++;

                console.MarkupLine($"[{ThemeService.PrimaryColor}]Welcome to Angelo's Portfolio.[/] [grey](SSH Edition)[/]");
                lineCount++;
                console.MarkupLine("[grey]Type [yellow]/help[/] for command list or start chatting with the AI.[/]");
                lineCount++;

                RenderFooter(console);
                lineCount++;
                break;

            case ChromeVariant.Compact:
                RenderCompactStatusLine(console);
                lineCount++;
                break;

            case ChromeVariant.None:
                // Nothing but the prompt - the terminal is too small for any chrome.
                break;
        }

        // 4. Only reserve a scrolling region when the layout decided there's a usable amount of
        // content room for it (see ChromeLayoutPlanner's invariants). Otherwise let output flow
        // into normal scrollback rather than a DECSTBM region too small to read - and make sure
        // no stale region survives from a previous, larger render (e.g. after a live resize).
        if (layout.FirstScrollLine == 0)
        {
            await _commandDispatcher.ResetScrollingRegionAsync(stream, ct);
            return;
        }

        int firstScrollLine = lineCount + 1;
        await _commandDispatcher.SetScrollingRegionAsync(stream, firstScrollLine, console.Profile.Height, ct);

        // 5. Move cursor to the start of the scrolling region
        await _commandDispatcher.MoveCursorAsync(stream, firstScrollLine, 1, ct);
    }

    /// <summary>
    /// Renders the Figlet banner panel and returns how many lines it actually occupies, measured
    /// from its own rendered segments rather than guessed. Replaces the old GetHeaderLineCount,
    /// which hard-coded 8 or 5 depending on width and admitted in a comment that it was an estimate.
    /// </summary>
    private static int RenderHeader(IAnsiConsole console)
    {
        var figlet = new FigletText("H I M")
            .Centered()
            .Color(ThemeService.PrimaryColor);

        var panel = new Panel(figlet)
        {
            Header = new PanelHeader($"[{ThemeService.AccentColor}] Heuristic Interactive Mockup [/]", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(ThemeService.PrimaryColor)
        };

        int lines = MeasureLines(panel, console);
        console.Write(panel);
        return lines;
    }

    /// <summary>
    /// Renders an <see cref="IRenderable"/> to segments using the same pipeline Spectre uses to
    /// actually draw it, and counts the resulting lines via <see cref="Segment.SplitLines(System.Collections.Generic.IEnumerable{Segment})"/>.
    /// This is the "measure, don't guess" seam: the number this returns is produced by the same
    /// code that draws the content, so it can't drift out of sync with what the terminal receives.
    /// </summary>
    private static int MeasureLines(IRenderable renderable, IAnsiConsole console)
    {
        var segments = renderable.GetSegments(console);
        return Segment.SplitLines(segments).Count;
    }

    private void RenderStatusBar(IAnsiConsole console)
    {
        var model = _modelDisplayName;
        var theme = ThemeService.CurrentTheme.ToString().ToUpper();

        var status = $"[{ThemeService.PrimaryColor}]●[/] MODEL: [white]{model}[/]  |  [{ThemeService.SecondaryColor}]▓[/] THEME: [white]{theme}[/]  |  [{ThemeService.AccentColor}]♢[/] SSH: [white]ACTIVE[/]";
        console.MarkupLine(status);
    }

    /// <summary>
    /// The single status line drawn for <see cref="ChromeVariant.Compact"/> terminals - too short
    /// for the full Figlet header, but wide enough to show what's running.
    /// </summary>
    private void RenderCompactStatusLine(IAnsiConsole console)
    {
        var model = _modelDisplayName;
        console.MarkupLine($"[{ThemeService.PrimaryColor}]●[/] HIM [grey]│[/] [white]{model}[/] [grey]│[/] [{ThemeService.AccentColor}]SSH ACTIVE[/]");
    }

    private void RenderFooter(IAnsiConsole console)
    {
        var fact = _funFacts[_funFactIndex % _funFacts.Length];
        _funFactIndex++;
        console.MarkupLine($"[grey]──[/] {fact} [grey]──[/]");
    }
}
