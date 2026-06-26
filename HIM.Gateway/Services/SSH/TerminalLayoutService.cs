using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;
using System.Text;

namespace HIM.Gateway.Services.SSH
{
    public class TerminalLayoutService(ICommandDispatcherHelper commandDispatcher) : ITerminalLayoutService
    {
        private readonly ICommandDispatcherHelper _commandDispatcher = commandDispatcher;

        public async Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            // 1. Reset Terminal and clear
            await stream.WriteAsync(Encoding.UTF8.GetBytes("\x1b[2J\x1b[H"), ct);
            console.Clear();

            // 2. Render the Fixed Header
            RenderHeader(console);
            console.Write(new Rule("[yellow]HEURISTIC INTERACTIVE MOCKUP[/]").Centered());

            // 3. Set the Scrolling Region (Line Top+1 to Bottom)
            int top = GetHeaderHeight(console.Profile.Width);
            await _commandDispatcher.SetScrollingRegionAsync(stream, top + 1, console.Profile.Height, ct);

            // 4. Move Cursor to the start of the Scrolling Zone
            await _commandDispatcher.MoveCursorAsync(stream, top + 1, 1, ct);

            console.MarkupLine("[bold white]Welcome to Angelo's Portfolio.[/] [grey](SSH Edition)[/]");
            console.MarkupLine("[grey]Type [white]/help[/] for command list or start chatting with the AI.[/]");
            console.WriteLine();
        }
        private int GetHeaderHeight(int terminalWidth)
        {
            return (terminalWidth > 60) ? 8 : 3;
        }

        private void RenderHeader(IAnsiConsole console)
        {
            if (console.Profile.Width >= 60)
            {
                console.Write(
                    new FigletText("H I M")
                        .Centered()
                        .Color(Color.Cyan1));
            }
            else
            {
                console.Write(
                    new Text("--- H I M ---", new Style(Color.Cyan1, decoration: Decoration.Bold))
                    .Centered());
            }
        }
    }
}
