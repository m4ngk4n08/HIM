using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    /// <summary>
    /// The entry point for the TUI Game Engine.
    /// </summary>
    /// <param name="_gameFactoryService"></param>
    public sealed class GameCommandService(
        IGameFactoryService _gameFactoryService,
        IGameVisualService _gameVisualService,
        ICommandDispatcherHelper _commandDispatcherHelper) : IGameCommandService
    {

        public async Task ExecuteAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            // 1. List available games
            var availableGames = _gameFactoryService.GetAvailableGames().ToList();
            if (!availableGames.Any())
            {
                console.MarkupLine("[red]No games are currently installed in the engine.[/]");
                return;
            }

            console.WriteLine();
            console.Write(new Rule("[bold cyan]H.I.M GAME LIBRARY[/]").RuleStyle("cyan"));
            console.MarkupLine("[grey]Select a challenge to begin: [/]");
            console.WriteLine();

            for (int i = 0; i < availableGames.Count; i++)
            {
                console.MarkupLine($"[bold yellow]{i + 1}[/]. [white]{availableGames[i].name}[/] - [grey]{availableGames[i].description}[/]");
            }

            console.WriteLine();
            console.Write(new Text("Enter number or name: ", new Style(Color.Green)));

            // 2. Get user choice

            string choice;

            try
            {

                choice = await _commandDispatcherHelper.ReadInputManualAsync(console, stream, ct);
            }
            catch (OperationCanceledException) { return; }

            string trimmedChoice = choice.Trim();
            IGameService? selectedGame = null;

            // Try parsing as number first
            if (int.TryParse(trimmedChoice, out int index) && index > 0 && index <= availableGames.Count)
            {
                selectedGame = _gameFactoryService.GetGame(availableGames[index - 1].name);
            }
            else
            {
                // Try parsing as name
                selectedGame = _gameFactoryService.GetGame(trimmedChoice);
            }

            if(selectedGame == null)
            {
                console.MarkupLine($"[red]Error:[/] '{trimmedChoice}' is not a valid game selection");
                return;
            }

            try
            {
                await _gameVisualService.ShowTransitionAsync(console, selectedGame.Name);

                if(selectedGame.Name.Equals(4) || selectedGame.Name.Equals("Pac-Man"))
                {

                    console.MarkupLine("[yellow]Creation of pacman is in progress. Comback later.[/]");
                    console.MarkupLine("[grey]/help to explore more of the contents.[/]");
                    await selectedGame.ShutdownAsync();
                }
                else
                {
                    await selectedGame.InitializeAsync(ct);
                    await selectedGame.ExecuteAsync(console, stream, ct);
                    await selectedGame.ShutdownAsync();
                }

            }
            catch (OperationCanceledException)
            {
                // Normal exit
            }
            catch(Exception ex)
            {
                console.MarkupLine($"[bold red]Game Error:[/] {Markup.Escape(ex.Message)}");
                // Log exception
            }
            
        }
             
    }
}
