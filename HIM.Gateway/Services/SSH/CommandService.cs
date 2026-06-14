using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH
{
    public class CommandService : ICommandService
    {
        private readonly IAiClientService _aiClientService;
        private readonly KnowledgeBaseSettings _kbSettings;
        private PortfolioData? _data;
        private readonly ConditionalWeakTable<IAnsiConsole, UserCooldownState> _cooldowns = new();
        private readonly TimeSpan _cooldownDuration = TimeSpan.FromSeconds(3);

        private class UserCooldownState { public DateTime LastQuery { get; set; } }

        public CommandService(
            IAiClientService aiClientService,
            IOptions<KnowledgeBaseSettings> kbSettings)
        {
            _aiClientService = aiClientService;
            _kbSettings = kbSettings.Value;
            LoadKnowledgeBase();
        }

        private void LoadKnowledgeBase()
        {
            try
            {
                if (!File.Exists(_kbSettings.FilePath)) return;

                var json = File.ReadAllText(_kbSettings.FilePath);
                _data = JsonSerializer.Deserialize<PortfolioData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Internal error: KB load failed: {ex.Message}");
            }
        }

        public async Task ProcessCommandAsync(IAnsiConsole console, string command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            if(_data == null)
            {
                console.MarkupLine($"[red]Error:[/] Knowledge base file not found or corrupted.");
                return;
            }

            var table = new Table();

            switch (command.ToLower())
            {
                case "/help": ShowHelp(console, table); break;
                case "/projects": ShowProjects(console, table); break;
                case "/about": ShowAbout(console); break;
                case "/skills": ShowSkills(console, table); break;
                case "/experience": ShowExperience(console); break;
                case "/clear": console.Clear();break;
                case "/exit":
                    console.MarkupLine("[red]Closing connection... Goodbye![/]");
                    throw new OperationCanceledException();
                default:
                    if(IsRateLimited(console))
                    {
                        console.MarkupLine("[yellow]![/] [grey]Neural Link is cooling down.. please wait");
                        break;
                    }
                    await HandleAiChatAsync(console, command, ct);
                    break;
            }
        }

        private bool IsRateLimited(IAnsiConsole console)
        {
            // Get or create the state for this specific user
            var state = _cooldowns.GetOrCreateValue(console);
            var now = DateTime.UtcNow;

            if(now - state.LastQuery < _cooldownDuration)
            {
                return true; // still in cooldown
            }

            state.LastQuery = now;
            return false;
        }

        private async Task HandleAiChatAsync(IAnsiConsole console, string question, CancellationToken ct)
        {
            console.WriteLine();
            console.Write(new Markup("[cyan1]AI:[/]"));

            try
            {
                // Initialize the stream but don't pull data yet
                var responsesStream = _aiClientService.GetAiResponseAsync(question, ct);
                await using var enumerator = responsesStream.GetAsyncEnumerator(ct);

                // Show the spinner WHILE waiting for the first chunk to arrive.
                bool hasData = await console.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan1"))
                    .StartAsync("Thinking..", async ctx =>
                    {
                        // this keeps the spinner spinning until the AI actually responds;
                        return await enumerator.MoveNextAsync();
                    });

                if(hasData)
                {
                    // Render the first chunk immediately
                    console.Write(new Text(enumerator.Current));

                    // Render the rest with a "typing" effect
                    while(await enumerator.MoveNextAsync())
                    {
                        console.Write(new Text(enumerator.Current));

                        // this tiny delay(20ms) creates the smooth typing animation
                        // even if the network is fast or buffering;
                        await Task.Delay(20, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                // SENIOR FIX: Always .EscapeMarkup() on dynamic error strings
                var safeMessage = ex.Message.EscapeMarkup();
                console.MarkupLine($"[red]Error: {safeMessage}[/]");
            }

            console.WriteLine();
            console.WriteLine();
        }

        private void ShowExperience(IAnsiConsole console)
        {
            var tree = new Tree("[bold yellow]CAREER JOURNEY[/]").Guide(TreeGuide.Line);

            foreach (var job in _data!.Experiences)
            {
                var node = tree.AddNode(
                    $"[bold cyan]{job.Position?.EscapeMarkup()}[/] @ " +
                    $"[white]{job.Company?.EscapeMarkup()}[/] " +
                    $"[grey]({job.Duration?.EscapeMarkup()})[/]");
                foreach (var highlights in job.Highlights)
                {
                    node.AddNode(highlights.EscapeMarkup());
                }
            }

            console.Write(tree);
        }

        private void ShowSkills(IAnsiConsole console, Table table)
        {
            table.Border(TableBorder.Rounded).Title("[bold blue]TECH STACK[/]");
            table.AddColumn("Category").AddColumn("Technologies");

            foreach (var category in _data!.TechnicalSkills)
            {
                table.AddRow(
                    $"[yellow]{char.ToUpper(category.Key[0]) + category.Key[1..]}[/]",
                    string.Join(", ", category.Value)
                    );
            }
            console.Write(table);
        }

        private void ShowHelp(IAnsiConsole console, Table table)
        {
            table.Border(TableBorder.Rounded).Title("[yellow]COMMANDS[/]");
            table.AddColumn("Command").AddColumn("Description");
            table.AddRow("/about", "Personal profile").AddRow("/experience", "Work history")
                .AddRow("/skills", "Tech Stack").AddRow("/projects", "Live projects")
                .AddRow("/clear", "Clear screen").AddRow("/exit", "Logout");
            console.Write(table);
        }

        private void ShowProjects(IAnsiConsole console, Table table)
        {
            table.Border(TableBorder.DoubleEdge).Title("[bold white]PROJECTS[/]");
            table.AddColumn("[yellow]Name[/]").AddColumn("[yellow]Stack[/]").AddColumn("[yellow]Status[/]");

            foreach(var proj in _data!.Projects)
            {
                table.AddRow($"[bold]{proj.Name}[/]", proj.Stack, $"[green]{proj.Status}[/]");
            }

            console.Write(table);

        }

        private void ShowAbout(IAnsiConsole console)
        {
            var p = _data!.PersonalInfo;
            var layout = new Rows(
                new Markup($"[cyan]{p.Summary}[/]\n"),
                new Rule().RuleStyle("grey"),
                new Grid()
                    .AddColumn().AddColumn()
                    .AddRow("[grey]Location:[/]", $"[white]{p.Location}[/]")
                    .AddRow("[grey]GitHub:[/]", $"[blue]{p.Contact.GetValueOrDefault("github", "N/A")}[/]")
                );

            console.Write(new Panel(layout)
                .Header($"[bold cyan] {p.Name.ToUpper()} // {p.Role.ToUpper()} [/]")
                .Border(BoxBorder.Rounded).BorderColor(Color.Cyan1).Expand());
                
        }
    }
}
