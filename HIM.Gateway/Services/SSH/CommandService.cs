using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH
{
    public class CommandService : ICommandService
    {
        private PortfolioData? _data;
        private readonly IAiClientService _aiClientService;
        private readonly IGameCommandService _gameCommandService;
        private readonly IMenuCommandService _menuCommandService;
        private readonly IStatsCommandService _statsCommandService;
        private readonly IMatrixCommandService _matrixCommandService;
        private readonly KnowledgeBaseSettings _kbSettings;
        private readonly TimeSpan _cooldownDuration = TimeSpan.FromSeconds(3);
        private readonly ConditionalWeakTable<IAnsiConsole, UserCooldownState> _cooldowns = new();

        private class UserCooldownState { public DateTime LastQuery { get; set; } }

        public CommandService(
            IAiClientService aiClientService,
            IGameCommandService gameCommandService,
            IMenuCommandService menuCommandService,
            IStatsCommandService statsCommandService,
            IMatrixCommandService matrixCommandService,
            IOptions<KnowledgeBaseSettings> kbSettings)
        {
            _kbSettings = kbSettings.Value;
            _aiClientService = aiClientService;
            _gameCommandService = gameCommandService;
            _menuCommandService = menuCommandService;
            _statsCommandService = statsCommandService;
            _matrixCommandService = matrixCommandService;
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

        public async Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct)
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
                // Static commands
                case "/help": ShowHelp(console, table); break;
                case "/clear": console.Clear();break;

                // Command Dispatch:
                case "/menu": await _menuCommandService.ExecuteAsync(console, stream, _data, ct); break;
                case "/stats": await _statsCommandService.ExecuteAsync(console, _data, ct); break;
                case "/matrix": await _matrixCommandService.ExecuteAsync(console, stream, ct); break;
                case "/game": await _gameCommandService.ExecuteAsync(console, stream, ct); break;
                
                // Connection teardown
                case "/exit":
                    console.MarkupLine("[red]Closing connection... Goodbye![/]");
                    throw new OperationCanceledException();
                
                // Chat integration fallback
                default:
                    if(IsRateLimited(console))
                    {
                        console.MarkupLine($"[yellow]![/] [grey]{Markup.Escape("Neural Link is cooling down.. please wait")}[/]");
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

        private void ShowHelp(IAnsiConsole console, Table table)
        {
            table.Border(TableBorder.Rounded).Title("[yellow]COMMANDS[/]");
            table.AddColumn("Command").AddColumn("Description");
            table.AddRow("/menu", "Interactive navigation menu")
                 .AddRow("/stats", "Developer RPG stats sheet")
                 .AddRow("/matrix", "Digital rain animation")
                 .AddRow("/game", "Developer trivia game")
                 .AddRow("/clear", "Clear screen").AddRow("/exit", "Logout");
            console.Write(table);
        }
    }
}
