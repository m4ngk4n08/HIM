using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;
using System.Text;

namespace HIM.Gateway.Services.SSH;

public class TerminalLayoutService : ITerminalLayoutService
{
    private readonly ICommandDispatcherHelper _commandDispatcher;
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

    public TerminalLayoutService(ICommandDispatcherHelper commandDispatcher)
    {
        _commandDispatcher = commandDispatcher;
    }

    public async Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
    {
        // 1. Reset terminal and clear
        await stream.WriteAsync(Encoding.UTF8.GetBytes("\x1b[2J\x1b[H"), ct);
        console.Clear();

        // 2. Render all static parts and track line count
        int lineCount = 0;

        // Header (panel + tagline)
        RenderHeader(console);
        lineCount += GetHeaderLineCount(console);
        console.WriteLine();
        lineCount++;

        // Status bar
        RenderStatusBar(console);
        lineCount++;
        console.WriteLine();
        lineCount++;

        // Welcome
        console.MarkupLine($"[{ThemeService.PrimaryColor}]Welcome to Angelo's Portfolio.[/] [grey](SSH Edition)[/]");
        lineCount++;
        console.MarkupLine("[grey]Type [yellow]/help[/] for command list or start chatting with the AI.[/]");
        lineCount++;
        console.WriteLine();
        lineCount++;

        // Footer
        RenderFooter(console);
        lineCount++;
        console.WriteLine();
        lineCount++;

        // 3. The first line of the scrolling region is the next line (1‑based)
        int firstScrollLine = lineCount + 1;

        // Clamp to terminal height
        if (firstScrollLine > console.Profile.Height)
            firstScrollLine = console.Profile.Height;

        // 4. Set scrolling region from firstScrollLine to bottom
        await _commandDispatcher.SetScrollingRegionAsync(stream, firstScrollLine, console.Profile.Height, ct);

        // 5. Move cursor to the start of the scrolling region
        await _commandDispatcher.MoveCursorAsync(stream, firstScrollLine, 1, ct);
    }

    private void RenderHeader(IAnsiConsole console)
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
        console.Write(panel);

        console.MarkupLine($"[grey]▸ [/][italic {ThemeService.SecondaryColor}]\"For a better experience, resize your window (Ctrl+- / Cmd+-).\"[/]");
    }

    private void RenderStatusBar(IAnsiConsole console)
    {
        var model = "llama3.3‑70b (Groq)";
        var theme = ThemeService.CurrentTheme.ToString().ToUpper();

        var status = $"[{ThemeService.PrimaryColor}]●[/] MODEL: [white]{model}[/]  |  [{ThemeService.SecondaryColor}]▓[/] THEME: [white]{theme}[/]  |  [{ThemeService.AccentColor}]♢[/] SSH: [white]ACTIVE[/]";
        console.MarkupLine(status);
    }

    private void RenderFooter(IAnsiConsole console)
    {
        var fact = _funFacts[_funFactIndex % _funFacts.Length];
        _funFactIndex++;
        console.MarkupLine($"[grey]──[/] {fact} [grey]──[/]");
    }

    private int GetHeaderLineCount(IAnsiConsole console)
    {
        // Estimate lines: Figlet panel (varies) + tagline (1)
        // Figlet panel: ~5-6 lines depending on width, plus padding.
        // Conservative estimate.
        return console.Profile.Width > 60 ? 8 : 5;
    }
}