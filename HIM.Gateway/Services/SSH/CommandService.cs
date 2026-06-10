using System;
using System.Collections.Generic;
using System.Text;
using HIM.Gateway.Services.SSH.Interfaces;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH
{
    public class CommandService : ICommandService
    {
        private readonly IAiClientService _aiClientService;

        public CommandService(IAiClientService aiClientService)
        {
            _aiClientService = aiClientService;
        }

        public async Task ProcessCommandAsync(IAnsiConsole console, string command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            var table = new Table();

            switch (command.ToLower())
            {
                case "/help":
                    ShowHelp(console, table);
                    break;

                case "/projects":
                    ShowProjects(console, table);
                    break;

                case "/about":
                    ShowAbout(console);
                    break;

                case "/skills":
                    ShowSkills(console);
                    break;

                case "/experience":
                    ShowExperience(console);
                    break;

                case "/clear":
                    console.Clear();
                    break;

                case "/exit":
                    console.MarkupLine("[red]Closing connection... Goodbye![/]");
                    throw new OperationCanceledException();

                default:
                    await HandleAiChatAsync(console, command, ct);
                    break;
            }
        }

        private async Task HandleAiChatAsync(IAnsiConsole console, string question, CancellationToken ct)
        {
            console.WriteLine();
            console.Write(new Markup("[cyan1]AI:[/]"));

            try
            {
                // Show the spinner while waiting for the network/AI to respond
                await console.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Thinking...", async ctx =>
                    {
                        // Wait for the first chunk to arrive to "break" the silence
                        await Task.Delay(500, ct);
                    });

                await foreach (var chunk in _aiClientService.GetAiResponseAsync(question, ct))
                {
                    console.Write(chunk);
                }
            }
            catch (Exception ex)
            {
                console.MarkupLine($"[red]Error communicating with AI Service: {ex.Message}");
            }

            console.WriteLine();
            console.WriteLine();
        }

        private void ShowExperience(IAnsiConsole console)
        {
            console.WriteLine();

            // Create the root of the tree
            var root = new Tree("[bold yellow]CAREER JOURNEY[/]")
                .Style("grey")
                .Guide(TreeGuide.Line);

            // Add company branches
            var current = root.AddNode("[bold cyan]Senior Full-Stack Engineer @ TechCorp[/] [grey](Present)[/]");
            current.AddNode("Architecting [green]distributed microservices[/] using .NET 10 and gRPC.");
            current.AddNode("Implementing a [yellow]RAG-based AI system[/] for internal knowledge management.");
            current.AddNode("Optimized system latency by [bold white]35%[/] through custom caching strategies.");

            // --- Role 2 ---
            var previous = root.AddNode("[bold white]Software Developer @ InnovationHub[/] [grey](2021 - 2023)[/]");
            previous.AddNode("Developed and maintained mission-critical [blue]ERP solutions[/].");
            previous.AddNode("Migrated legacy monolithic apps to [magenta]Dockerized containers[/].");
            previous.AddNode("Led a team of 3 developers for a [green]real-time analytics[/] dashboard.");

            // --- Role 3 ---
            var startup = root.AddNode("[bold white]Junior Dev @ StartUp Labs[/] [grey](2019 - 2021)[/]");
            startup.AddNode("Built interactive front-end components using [blue]React[/] and [cyan]TypeScript[/].");
            startup.AddNode("Integrated third-party [yellow]Stripe APIs[/] for payment processing.");

            // render tree
            console.Write(root);
            console.WriteLine();
        }

        private void ShowSkills(IAnsiConsole console)
        {
            console.WriteLine();

            var chart = new BarChart()
                .Width(60)
                .Label("[bold blue]CORE TECHNICAL PROFICIENCIES[/]")
                .CenterLabel();

            // Adding items with values out of 100 for a clean percentage look
            chart.AddItem("C# / .NET 10", 95, Color.Cyan1);
            chart.AddItem("System Architecture", 85, Color.Blue);
            chart.AddItem("AI / RAG Pipelines", 80, Color.Yellow);
            chart.AddItem("Cloud / DevOps", 75, Color.Green);
            chart.AddItem("Terminal UIs", 90, Color.Magenta1);

            console.Write(chart);
            console.WriteLine();
        }

        private void ShowHelp(IAnsiConsole console, Table table)
        {
            table
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Gray)
            .Title("[yellow]AVAILABLE COMMANDS[/]")
            .Expand();

            table.AddColumn("[cyan]Command[/]");
            table.AddColumn("[white]Description[/]");

            table.AddRow("/about", "Who is Angelo?");
            table.AddRow("/projects", "Display list of technical projects.");
            table.AddRow("/skills", "Display list of technical technical skills.");
            table.AddRow("/experience", "Display list of career experiences.");
            table.AddRow("/clear", "Reset the terminal view.");
            table.AddRow("/exit", "Terminate the SSH connection.");

            console.Write(table);
        }

        private void ShowProjects(IAnsiConsole console, Table table)
        {
            console.WriteLine();
                table 
               .Border(TableBorder.DoubleEdge)
               .BorderColor(Color.Cyan1)
               .Title("[bold white]TECHNICAL PROJECTS[/]")
               .Caption("[grey]Type a project name to ask the AI for more details[/]");

            table.AddColumn("[yellow]Project Name[/]");
            table.AddColumn("[yellow]Tech Stack[/]");
            table.AddColumn("[yellow]Status[/]");

            table.AddRow(
                "[bold]HIM Gateway[/]",
                "C#, .NET 10, SSH, Spectre.Console",
                "[green]Active[/]");

            table.AddRow(
                "[bold]Neural-RAG Service[/]",
                "Python, FastAPI, Llama3, Ollama",
                "[blue]Completed[/]");

            table.AddRow(
                "[bold]Heuristic-Shared[/]",
                "gRPC, Protobuf, Shared Architecture",
                "[yellow]Beta[/]");

            console.Write(table);
            console.WriteLine();
        }

        private void ShowAbout(IAnsiConsole console)
        {
            console.WriteLine();

            // Create the bio text
            var bio = new Markup(
                "I am a [cyan]Full-Stack Engineer[/] specialized in building [yellow]distributed systems[/] and " +
                "intelligent automation. My focus is on creating technical experiences that are not just functional, " +
                "but visually and architecturally elegant.\n\n" +
                "[grey]- 5+ Years in .NET Ecosystem[/]\n" +
                "[grey]- RAG & AI Integration Enthusiast[/]\n" +
                "[grey]- SSH & Terminal UI Advocate[/]"
                );

            // Create a small grid for "System Info" stats
            var stats = new Grid();
            stats.AddColumn();
            stats.AddColumn();
            stats.AddRow("[grey]Location:[/]", "[white]Manila, PH[/]");
            stats.AddRow("[grey]Availability:[/]", "[green]Open for Innovation[/]");
            stats.AddRow("[grey]Primary Language:[/]", "[blue]C# / TypeScript[/]");

            // Combine into a layout(using rows)
            var layout = new Rows(
                bio,
                new Rule().RuleStyle("grey"),
                stats
                );

            // Wrap everything in a polished Panel
            var panel = new Panel(layout)
                .Header("[bold cyan] PROFILE: ANGELO [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1)
                .Padding(1, 1, 1, 1)
                .Expand();

            console.Write(panel);
            console.WriteLine();
        }
    }
}
