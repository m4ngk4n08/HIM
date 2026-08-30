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
        "🧠 Powered by ONNX + Gemini",
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
        // boundary below reflects what was really drawn, not an estimate of it. Every markup
        // line is routed through RenderFittedMarkupLine (or RenderHeader's own MeasureLines
        // call), which shrinks its content until it measures as exactly one row at the
        // console's current width - so lineCount can never under-report a wrapped line.
        int lineCount = 0;

        switch (layout.Variant)
        {
            case ChromeVariant.Full:
                lineCount += RenderHeader(console);
                console.WriteLine();
                lineCount++;

                lineCount += RenderStatusBar(console);

                lineCount += RenderFittedMarkupLine(console,
                    text => $"[{ThemeService.PrimaryColor}]{Markup.Escape(text)}[/] [grey](SSH Edition)[/]",
                    "Welcome to Angelo's Portfolio.");
                lineCount += RenderFittedMarkupLine(console,
                    text => $"[grey]{Markup.Escape(text)}[/]",
                    "Type /help for command list or start chatting with the AI.");

                lineCount += RenderFooter(console);
                break;

            case ChromeVariant.Compact:
                lineCount += RenderCompactStatusLine(console);
                lineCount += RenderFittedMarkupLine(console,
                    text => $"[grey]{Markup.Escape(text)}[/]",
                    "/help for commands · or just type to chat with the AI");
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

    /// <summary>
    /// Writes a single-line markup value, shrinking <paramref name="content"/> (dropping whole
    /// trailing words first, then characters as a last resort, appending an ellipsis once
    /// anything is dropped) until <paramref name="buildMarkup"/>'s output measures as exactly one
    /// row at the console's current width. This is what makes "no chrome line may wrap" true by
    /// construction instead of by hand-picked width thresholds: it reuses the same
    /// <see cref="MeasureLines"/> seam <see cref="RenderHeader"/> uses, so a line can never be
    /// counted as one row while actually occupying more.
    /// </summary>
    private static int RenderFittedMarkupLine(IAnsiConsole console, Func<string, string> buildMarkup, string content)
    {
        string current = content;
        string markup = buildMarkup(current);

        while (MeasureLines(new Markup(markup), console) > 1)
        {
            int lastSpace = current.TrimEnd().LastIndexOf(' ');
            if (lastSpace > 0)
            {
                current = current[..lastSpace];
            }
            else if (current.Length > 1)
            {
                current = current[..^1];
            }
            else
            {
                current = string.Empty;
                markup = buildMarkup(current);
                break;
            }

            markup = buildMarkup(current + "…");
        }

        int lines = MeasureLines(new Markup(markup), console);
        console.MarkupLine(markup);
        return lines;
    }

    private int RenderStatusBar(IAnsiConsole console)
    {
        var theme = ThemeService.CurrentTheme.ToString().ToUpper();

        return RenderFittedMarkupLine(console,
            m => $"[{ThemeService.PrimaryColor}]●[/] MODEL: [white]{Markup.Escape(m)}[/]  |  " +
                 $"[{ThemeService.SecondaryColor}]▓[/] THEME: [white]{theme}[/]  |  " +
                 $"[{ThemeService.AccentColor}]♢[/] SSH: [white]ACTIVE[/]",
            _modelDisplayName);
    }

    /// <summary>
    /// The status line drawn for <see cref="ChromeVariant.Compact"/> terminals - too short for the
    /// full Figlet header, but wide enough to show what's running. A second pinned line (see the
    /// call site in <see cref="InitializeTerminalLayoutAsync"/>) restores the /help and
    /// just-type-to-chat hint that Full's welcome text carries, since Compact has no other room
    /// for it and chrome, unlike scrollback, stays visible for the whole session.
    /// </summary>
    private int RenderCompactStatusLine(IAnsiConsole console)
    {
        return RenderFittedMarkupLine(console,
            m => $"[{ThemeService.PrimaryColor}]●[/] HIM [grey]│[/] [white]{Markup.Escape(m)}[/] [grey]│[/] [{ThemeService.AccentColor}]SSH ACTIVE[/]",
            _modelDisplayName);
    }

    private int RenderFooter(IAnsiConsole console)
    {
        var fact = _funFacts[_funFactIndex % _funFacts.Length];
        _funFactIndex++;

        return RenderFittedMarkupLine(console,
            f => $"[grey]──[/] {Markup.Escape(f)} [grey]──[/]",
            fact);
    }
}
