using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.ServiceModel.CommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    internal sealed class StatsCommandService : IStatsCommandService
    {
        private readonly SkillProfile _profile;

        public StatsCommandService(
            IOptions<SkillProfile> profile)
        {
            _profile = profile.Value;
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

        public Task ExecuteAsync(IAnsiConsole console, PortfolioData data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            console.WriteLine();
            RenderIdentityCard(console, data.PersonalInfo);
            console.WriteLine();
            RenderSkillBars(console);
            console.WriteLine();
            RenderProjectSummary(console, data.Projects);
            console.WriteLine();

            return Task.CompletedTask;
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

        private void RenderSkillBars(IAnsiConsole console)
        {
            var chart = new BarChart()
                .Width(60)
                .Label("[bold yellow underline]SKILL PROFICIENCIES[/]");

            foreach(var item in _profile.Skills)
            {
                var (_, color) = GetProfileColor(item.Skill);
                var label = item.Skill.Length > 0
                    ? char.ToUpper(item.Skill[0]) + item.Skill[1..]
                    : item.Skill;
            }

            console.Write(chart);
        }

        private void RenderIdentityCard(IAnsiConsole console, PersonalInfo personalInfo)
        {
            var github = personalInfo.Contact?.GetValueOrDefault("github", "N/A") ?? "N/A";

            var grid = new Grid()
                .AddColumn(new GridColumn().NoWrap())
                .AddColumn()
                .AddRow("[grey]ROLE     :[/]", $"[bold white]{personalInfo.Role.EscapeMarkup()}[/]")
                .AddRow("[grey]LOCATION :[/]", $"[white]{personalInfo.Location.EscapeMarkup()}[/]")
                .AddRow("[grey]GITHUB   :[/]", $"[blue]{github.EscapeMarkup()}[/]")
                .AddRow("[grey]STATUS   :[/]", "[bold green]ONLINE[/]");

            console.Write(new Panel(grid)
                .Header($"[bold cyan] {personalInfo.Name.ToUpper().EscapeMarkup()} [/]")
                .Border(BoxBorder.Double)
                .BorderColor(Color.Cyan1)
                .Expand()
                );
        }
    }
}
