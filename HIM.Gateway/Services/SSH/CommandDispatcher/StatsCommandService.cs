using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.ServiceModel.CommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

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
                        
            RenderIdentityCard(console, data.PersonalInfo);
            console.WriteLine();
            await RenderSkillBars(console, cancellationToken);
            console.WriteLine();
            RenderProjectSummary(console, data.Projects);
            console.WriteLine();

        }

        private void RenderProjectSummary(IAnsiConsole console, List<ProjectItem> projects)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold white]PROJECT LOG[/]")
                .AddColumn("[yellow]Name[/]")
                .AddColumn("[yellow]Stack[/]")
                .AddColumn("[yellow]Status[/]");

            foreach(var project in projects)
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

        private async Task RenderSkillBars(IAnsiConsole console, CancellationToken ct)
        {
            var chart = new BarChart()
                .Width(60)
                .Label("[bold yellow underline]SKILL PROFICIENCIES[/]");

            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "command-dispatcher.json");

            if (!File.Exists(filePath))
            {
                console.MarkupLine("[red]Error:[/] SKill configuration file now found.");
                return;
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

                if(skills == null || !skills.Any())
                {
                    console.MarkupLine("[yellow]No skill profiles found in the configuration.[/]");
                    return;
                }


                foreach (var item in skills)
                {
                    var (_, color) = GetProfileColor(item.Skill);
                    var label = item.Skill.Length > 0
                        ? char.ToUpper(item.Skill[0]) + item.Skill[1..]
                        : item.Skill;

                    chart.AddItem(label, item.Compentency, color);
                }

                console.Write(chart);
            }
            catch (Exception)
            {

                throw;
            }

        }

        private void RenderIdentityCard(IAnsiConsole console, PersonalInfo personalInfo)
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

            var paddedPanel = new Padder(panel, new Padding(0, 2, 0, 0));

            console.Write(paddedPanel);
        }
    }
}
