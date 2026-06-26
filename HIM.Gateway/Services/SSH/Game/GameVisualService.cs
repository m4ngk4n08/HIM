using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Game
{
    internal sealed class GameVisualService : IGameVisualService
    {
        private readonly char[] _glitchChars = ['@', '#', '$', '%', '&', '*', '!', '?', '0', '1'];
        private readonly Random _rng = new();
        public void ApplyGameTheme(IAnsiConsole console)
        {
            console.Write(new Rule("[bold cyan]GAME ENGINE ACTIVE[/]").RuleStyle("cyan"));
        }

        public async Task PlayLevelUpAnimationAsync(IAnsiConsole console, string message)
        {
            console.MarkupLine($"[bold gold1]✨ {Markup.Escape(message)} ✨[/]");
            await Task.Delay(500);
        }

        public void RenderGameOver(IAnsiConsole console, string gameName, int currentScore, int maxScore, string reason, bool isNewHighScore)
        {
            console.WriteLine();

            // 1. Header Rule
            var headerColor = isNewHighScore ? Color.Gold1 : Color.Cyan1;
            console.Write(new Rule($"[bold {headerColor.ToString().ToLower()}] {gameName.ToUpper()} [/]").RuleStyle(headerColor.ToString().ToLower()));

            // 2. Calculate Rank
            double percentage = maxScore > 0 ? (double)currentScore / maxScore : 0;
            string rank = percentage switch
            {
                >= 0.9 => "[bold gold1]PRINCIPAL ENGINEER[/]",
                >= 0.7 => "[bold cyan]SENIOR DEV[/]",
                >= 0.5 => "[bold green]MID-LEVEL DEV[/]",
                >= 0.3 => "[bold yellow]JUNIOR DEV[/]",
                _ => "[bold red]INTERN[/]"
            };

            // 3. Build the Results Grid
            var grid = new Grid()
                    .AddColumn()
                    .AddColumn();
            grid.AddRow("[grey]Status    :[/]", isNewHighScore ? "[bold gold1]NEW HIGH SCORE! 🏆[/]" : "[grey]Session Ended[/]");
            grid.AddRow("[grey]Score    :[/]", $"[white]{currentScore} / {maxScore}[/]");
            grid.AddRow("[grey]Rank:    :[/]", rank);
            grid.AddRow("[grey]Reason:  :[/]", $"[white]{reason}[/]");

            // 4. Render the panel
            console.Write(new Panel(grid)
                .Header($"[bold {headerColor.ToString().ToLower()}] RESULTS [/]", Justify.Left)
                .Border(BoxBorder.Rounded)
                .BorderColor(headerColor)
                );

            console.WriteLine();
        }

        public async Task ShowTransitionAsync(IAnsiConsole console, string gameName)
        {
            // We use a live display to render the animation framse
            try
            {
                // Get the actual dimensions of the user's terminal window
                int width = console.Profile.Width;
                int height = console.Profile.Height;

                await console.Live(new Markup(""))
                .StartAsync(async ctx =>
                {
                    // 1. The "Glitch" phase
                    for (int frame = 0; frame < 15; frame++)
                    {
                        var grid = BuildGlitchGrid(width, height);
                        ctx.UpdateTarget(grid);
                        await Task.Delay(60);
                    }

                    // 2. The "Stabilization" Phase (The scanline/snap effect)
                    for (int frame = 0; frame < 10; frame++)
                    {
                        var grid = BuildStabilizingGrid(width, height, gameName, frame);
                        ctx.UpdateTarget(grid);
                        await Task.Delay(80);
                    }
                });

                console.Clear();
            }
            catch (Exception ex)
            {

                console.MarkupLine($"[bold red]Game Error:[/] {Markup.Escape(ex.Message)}");
            }
        }

        private Markup BuildStabilizingGrid(int width, int height, string gameName, int frame)
        {
            var sb = new StringBuilder();

            // Fill top and bottom with a few glitch lines to keep the energy up
            string color = frame < 4 ? "cyan" : "gold1";

            for (int y = 0; y < height; y++)
            {
                if (y == height / 2) // Middle line: The Game Name
                {
                    string nameLine = $"--- {gameName.ToUpper()} ---";
                    int padding = (width - nameLine.Length) / 2;
                    sb.Append(new string(' ', padding));
                    sb.Append($"[bold {color}]{nameLine}[/]");
                    sb.Append(new string(' ', width - padding - nameLine.Length));
                }
                else if (y == (height / 2) + 1) // Sub-line: Status
                {
                    string status = "SYSTEM ONLINE";
                    int padding = (width - status.Length) / 2;
                    sb.Append(new string(' ', padding));
                    sb.Append($"[green]{status}[/]");
                    sb.Append(new string(' ', width - padding - status.Length));
                }
                else // Other lines: Fading glitch
                {
                    if (_rng.Next(10) > 7) // Only render some glitch characters to reduce bandwidth
                    {
                        sb.Append($"[{color}]");
                        for (int x = 0; x < width; x++)
                        {
                            sb.Append(_rng.Next(2) == 0 ? ' ' : _glitchChars[_rng.Next(_glitchChars.Length)]);
                        }
                        sb.Append("[/]");
                    }
                    else
                    {
                        sb.Append(new string(' ', width));
                    }
                }
                sb.Append("\n");
            }
            return new Markup(sb.ToString());
        }

        private Markup BuildGlitchGrid(int width, int height)
        {
            var sb = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                // Alternate colors per line for that "high-energy" feel
                string color = (y % 2 == 0) ? "cyan" : "blue";
                sb.Append($"[{color}]");

                for (int x = 0; x < width; x++)
                {
                    sb.Append(_glitchChars[_rng.Next(_glitchChars.Length)]);
                }
                sb.Append("[/]\n");
            }
            return new Markup(sb.ToString());
        }

        public async Task ShowInstructionAsync(IAnsiConsole console, string gameName, string instructions, CancellationToken ct)
        {
            console.WriteLine();

            // Render instructions in a clean, centered panel
            console.Write(new Panel(new Text(instructions))

                .Header($"[bold cyan]HOW TO PLAY: {gameName.ToUpper()}[/]")
                .BorderColor(Color.Cyan1)
                .Padding(1, 1)
                .Expand()
                );

        }
    }
}
