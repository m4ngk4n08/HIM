using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.ServiceModel.CommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    internal sealed class StatsCommandService : IStatsCommandService
    {
        public StatsCommandService()
        {
        }

        private (double score, Color color) GetProfileColor(string skill)
        {
            var color = skill.ToLowerInvariant() switch
            {
                "backend" => Color.Cyan1,
                "frontend" => Color.Green,
                "devops" => Color.Yellow,
                "database" => Color.Blue,
                "ai" => Color.Magenta1,
                "mobile" => Color.Orange1,
                _ => Color.White
            };

            return (0, color);
        }

        public async Task ExecuteAsync(IAnsiConsole console, Stream stream, PortfolioData data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var identityCard = GetIdentityCard(data.PersonalInfo);
            var skillBars = await GetSkillBarsAsync(cancellationToken);

            // Responsive Layout Choice:
            // If the terminal has plenty of width (>= 95 chars), put Identity & Skills side-by-side.
            if (console.Profile.Width >= 95 && skillBars != null)
            {
                skillBars.Width(50);

                var columns = new Grid()
                    .AddColumn(new GridColumn().NoWrap()) // Column 1: Identity Card
                    .AddColumn(new GridColumn().Padding(4, 0, 0, 0)); // Column 2: Skill Bars

                columns.AddRow(identityCard, skillBars);
                console.Write(columns);
            }
            else
            {
                // Fallback for narrower terminals: Stack them vertically
                console.Write(new Padder(identityCard, new Padding(0, 1, 0, 0)));
                console.WriteLine();
                if (skillBars != null)
                {
                    skillBars.Width(Math.Min(60, console.Profile.Width - 10));
                    console.Write(skillBars);
                }
            }

            console.WriteLine();
            RenderProjectSummary(console, data.Projects);
            console.WriteLine();
        }

        private Panel GetIdentityCard(PersonalInfo personalInfo)
        {
            var github = personalInfo.Contact?.GetValueOrDefault("github", "N/A") ?? "N/A";

            var grid = new Grid()
                .AddColumn(new GridColumn().NoWrap())
                .AddColumn()
                .AddRow("[grey]Role:[/]", $"[bold white]{personalInfo.Role.EscapeMarkup()}[/]")
                .AddRow("[grey]Location:[/]", $"[white]{personalInfo.Location.EscapeMarkup()}[/]")
                .AddRow("[grey]Github:[/]", $"[blue]{github.EscapeMarkup()}[/]")
                .AddRow("[grey]Status:[/]", "[bold green]ONLINE[/]");

            var panel = new Panel(grid)
                .Header($"[bold cyan] {personalInfo.Name.ToUpper().EscapeMarkup()} [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1);

            return panel;
        }

        private async Task<BarChart?> GetSkillBarsAsync(CancellationToken ct)
        {
            var chart = new BarChart()
                .Label("[bold yellow underline]SKILL PROFICIENCIES[/]");

            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "command-dispatcher.json");

            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                string jsonString = await File.ReadAllTextAsync(filePath, ct);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var config = JsonSerializer.Deserialize<CommandDispatcherConfig>(jsonString, options);
                var skills = config?.SkillProfile?.Skills;

                if (skills == null || !skills.Any())
                {
                    return null;
                }

                foreach (var item in skills)
                {
                    var (_, color) = GetProfileColor(item.Skill);
                    var label = item.Skill.Length > 0
                        ? char.ToUpper(item.Skill[0]) + item.Skill[1..]
                        : item.Skill;

                    chart.AddItem(label, item.Compentency, color);
                }

                return chart;
            }
            catch
            {
                return null;
            }
        }

        private void RenderProjectSummary(IAnsiConsole console, List<ProjectItem> projects)
        {
            // --- VERTICAL BUDGET CALCULATION ---
            // Estimate how much vertical scrolling height we have left.
            int headerHeight = console.Profile.Height >= 28 ? 10 : 5;
            int statsGridHeight = (console.Profile.Width >= 95) ? 8 : 15;
            int reservedHeight = headerHeight + statsGridHeight + 3; // 3 lines of safety buffer
            int availableHeightForProjects = console.Profile.Height - reservedHeight;

            // Estimate how tall the rounded table will be with wrapped text
            int estimatedTableHeight = 6; // Borders + Title + Headers
            foreach (var project in projects)
            {
                // Estimate text wrapping of the "Stack" column
                int stackWidth = (int)(console.Profile.Width * 0.5);
                if (stackWidth < 10) stackWidth = 30;
                int wrapLines = (project.Stack.Length + stackWidth - 1) / stackWidth;
                estimatedTableHeight += Math.Max(1, wrapLines) + 1; // +1 for row line spacer
            }

            // If the estimated table is too tall to fit on the screen, use a clean compact fallback
            if (estimatedTableHeight > availableHeightForProjects || console.Profile.Height < 32)
            {
                console.MarkupLine("[bold yellow]PROJECT LOG[/]");
                foreach (var project in projects)
                {
                    var statusMarkup = project.Status.ToLowerInvariant() switch
                    {
                        "live" or "active" => $"[bold green]{project.Status.EscapeMarkup()}[/]",
                        "wip" => $"[yellow]{project.Status.EscapeMarkup()}[/]",
                        _ => $"[grey]{project.Status.EscapeMarkup()}[/]"
                    };

                    console.MarkupLine($"• [bold white]{project.Name.EscapeMarkup()}[/] ({statusMarkup})");

                    // Prevent long stack text from wrapping into multiple lines on short screens
                    var stack = project.Stack.EscapeMarkup();
                    int maxStackLength = console.Profile.Width - 12;
                    if (stack.Length > maxStackLength && maxStackLength > 10)
                    {
                        stack = stack[..maxStackLength] + "...";
                    }
                    console.MarkupLine($"  [grey]Stack:[/] {stack}");
                }
                return;
            }

            // --- FULL BEAUTIFUL TABLE (When space allows) ---
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold white]PROJECT LOG[/]")
                .AddColumn("[yellow]Name[/]")
                .AddColumn("[yellow]Stack[/]")
                .AddColumn("[yellow]Status[/]");

            foreach (var project in projects)
            {
                var statusMarkup = project.Status.ToLowerInvariant() switch
                {
                    "live" or "active" => $"[bold green]{project.Status.EscapeMarkup()}[/]",
                    "wip" => $"[yellow]{project.Status.EscapeMarkup()}[/]",
                    _ => $"[grey]{project.Status.EscapeMarkup()}[/]"
                };

                table.AddRow(
                    $"[bold]{project.Name.EscapeMarkup()}[/]",
                    project.Stack.EscapeMarkup(),
                    statusMarkup
                );
            }

            console.Write(table);
        }
    }
}