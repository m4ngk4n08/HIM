using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    internal sealed class MenuCommandService : IMenuCommandService
    {
        public MenuCommandService(ICommandDispatcherHelper commandDispatcherHelper)
        {
            _commandDispatcherHelper = commandDispatcherHelper;
        }

        private static readonly string[] MenuOptions =
            [
                "About Me",
                "Skills & Tech Stack",
                "Work Experience",
                "Projects",
                "Developer Stats",
                "─────────────────",
                "Exit"
            ];

        private readonly ICommandDispatcherHelper _commandDispatcherHelper;

        public async Task ExecuteAsync(IAnsiConsole console, Stream stream, PortfolioData data, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                console.Clear();
                RenderMenuHeader(console);

                for (int i = 0; i < MenuOptions.Length; i++)
                {
                    console.WriteLine($"{i + 1} {MenuOptions[i]}");
                }

                console.WriteLine("─────────────────");
                console.Write(new Text("Selection: ", new Style(Color.Green)));

                string choice = await _commandDispatcherHelper.ReadInputManualAsync(console, stream, ct);

                // Map the number input back to the MenuOptions string
                if (int.TryParse(choice, out int index) && index > 0 && index <= MenuOptions.Length)
                {
                    string selectedOption = MenuOptions[index - 1];

                    switch (selectedOption)
                    {
                        case "About Me": ShowAbout(console, data); break;
                        case "Skills & Tech Stack": ShowSkills(console, data); break;
                        case "Work Experience": ShowExperience(console, data); break;
                        case "Projects": ShowProjects(console, data); break;
                        case "Developer Stats": ShowStats(console, data); break;
                        case "Exit": return;
                    }
                }
                else if (choice.ToLower() == "exit")
                {
                    return;
                }

                console.WriteLine();
                console.MarkupLine("[grey]Press [white]Enter[/] to return to menu...[/]");
                await _commandDispatcherHelper.ReadInputManualAsync(console, stream, ct); // wait for enter
            }
        }
            

        private void ShowStats(IAnsiConsole console, PortfolioData data)
        {
            console.MarkupLine("[bold cyan]Developer Stats are available via [white]/Stats[/].[/]");
        }

        private void ShowProjects(IAnsiConsole console, PortfolioData data)
        {
            var table = new Table()
                .Border(TableBorder.DoubleEdge)
                .Title("[bold white]PROJECTS[/]");
            table.AddColumn("[yellow]Name[/]").AddColumn("[yellow]Stack[/]").AddColumn("[yellow]Status[/]");

            foreach(var proj in data.Projects)
            {
                table.AddRow($"[bold]{proj.Name.EscapeMarkup()}[/]", proj.Stack.EscapeMarkup(),
                    $"[green]{proj.Status.EscapeMarkup()}[/]");
            }

            console.Write(table);
        }

        private void ShowExperience(IAnsiConsole console, PortfolioData data)
        {
            var tree = new Tree("[bold yellow]CAREER JOURNEY[/]").Guide(TreeGuide.Line);

            foreach(var job in data.Experiences)
            {
                var node = tree.AddNode(
                    $"[bold cyan]{job.Position?.EscapeMarkup()} @ [/]" +
                    $"[white]{job.Company?.EscapeMarkup()}[/] " +
                    $"[grey]({job.Duration?.EscapeMarkup()})[/]"
                    );
                foreach (var highlights in job.Highlights)
                {
                    node.AddNode(highlights.EscapeMarkup());
                }
            }

            console.Write(tree);
        }

        private void ShowSkills(IAnsiConsole console, PortfolioData data)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold blue]TECH STACK[/]");

            table.AddColumn("Category").AddColumn("Technologgies");

            foreach(var category in data.TechnicalSkills)
            {
                table.AddRow(
                    $"[yellow]{char.ToUpper(category.Key[0]) + category.Key[1..]}[/]",
                    string.Join(", ", category.Value).EscapeMarkup()
                    );
            }

            console.Write(table);
        }

        private void ShowAbout(IAnsiConsole console, PortfolioData data)
        {
            var p = data.PersonalInfo;
            var layout = new Rows(
                new Markup($"[cyan]{p.Summary.EscapeMarkup()}[/]\n"),
                new Rule().RuleStyle("grey"),
                new Grid()
                    .AddColumn().AddColumn()
                    .AddRow("[grey]Location:[/]", $"[white]{p.Location.EscapeMarkup()}[/]")
                    .AddRow("[grey]Github:[/]", $"[blue]{p.Contact.GetValueOrDefault("github", "N/A").EscapeMarkup()}[/]")
                );

            console.Write(new Panel(layout)
                .Header($"[bold cyan]{p.Name.ToUpper().EscapeMarkup()} // {p.Role.ToUpper().EscapeMarkup()} [/]")
                .Border(BoxBorder.Rounded).BorderColor(Color.Cyan1).Expand()
                );
        }

        private void RenderMenuHeader(IAnsiConsole console)
        {
            console.Write(new Rule("[bold cyan]H I M - Portfolio Navigator[/]").RuleStyle("cyan").Centered());
            console.WriteLine();
        }
    }
}
