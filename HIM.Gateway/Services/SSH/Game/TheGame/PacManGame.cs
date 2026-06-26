using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using HIM.Gateway.Services.ServiceModel.Enums;
using HIM.Gateway.Services.SSH.Game.TheGame.PacMan;
using HIM.Gateway.Services.SSH.Interfaces;

namespace HIM.Gateway.Services.SSH.Game.TheGame
{
    public class PacManGame(
        IGameInputService gameInputService,
        IGameVisualService gameVisualService,
        IGameScoreService gameScoreService,
        ICommandDispatcherHelper commandDispatcherHelper,
        ITerminalLayoutService terminalLayoutService
        ) : IGameService
    {
        private readonly IGameInputService _gameInputService = gameInputService;
        private readonly IGameVisualService _gameVisualService = gameVisualService;
        private readonly IGameScoreService _gameScoreService = gameScoreService;
        private readonly ICommandDispatcherHelper _commandDispatcherHelper = commandDispatcherHelper;
        private readonly ITerminalLayoutService _terminalLayoutService = terminalLayoutService;

        public string Name => "Pac-Man";
        public string Description => "Classic ASCII Pac-Man maze navigation.";

        private GameState? _gameState;

        public async Task InitializeAsync(CancellationToken ct)
        {
            // Initialize a simple 10x10 maze
            _gameState = new GameState(20, 20);
            _gameState.PacManPosition = new Position(5, 5);

            // Populate the grid
            for (int y = 0; y < _gameState.Height; y++)
            {
                for(int x = 0; x < _gameState.Width; x++)
                {
                    // Simple maze: walls on borders
                    if(y == 0 || y == _gameState.Height - 1 || x == 0 || x == _gameState.Width - 1)
                    {
                        _gameState.Grid[y, x] = TileType.Wall;
                    }
                    else
                    {
                        // Fille the rest with pellets
                        _gameState.Grid[y, x] = TileType.Pellet;
                    }
                }
            }

            await Task.CompletedTask;
        }

        public async Task ExecuteAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            await _terminalLayoutService.InitializeTerminalLayoutAsync(console, stream, ct);
            if (_gameState == null) await InitializeAsync(ct);
            
            _gameVisualService.ApplyGameTheme(console);
            await _gameVisualService.ShowInstructionAsync(console, Name, "Use W/A/S/D to move. Press escape to quit.", ct);
            bool quitRequested = false;

            while (!ct.IsCancellationRequested && !_gameState!.IsGameOver)
            {
                // 1. Render State
                Render(console);

                // 2. Capture Input
                var input = await _gameInputService.GetNextInputAsync(stream, ct);
                if (input == GameInput.None) continue;

                if (IsQuit(input))
                {
                    console.MarkupLine("[yellow]Quit the game?[/]");
                    var raw = await _commandDispatcherHelper.ReadInputManualAsync(console, stream, ct);
                    // Secure quit confirmation
                    if (raw.Trim().ToLower().Equals("y"))
                    {
                        quitRequested = true;
                        break;
                    }
                    else if (raw.Trim().ToLower().Equals("n"))
                    {
                        continue;
                    }
                }

                // 3. Update Player (move + consume)
                UpdatePlayer(input);

                // 4. Update ghosts
                UpdateGhosts();

                // 5. Collision Check (pacman vs ghost)
                if(CheckCollision())
                {
                    _gameState!.Lives--;
                    if(_gameState.Lives > 0)
                    {
                        _gameState.ResetPositions();
                        console.MarkupLine("[red]List a life![/]");

                        // Small pause to let the user react
                        await Task.Delay(1000, ct);
                    }
                    else
                    {
                        _gameState.IsGameOver = true;
                    }
                }

            }

            string endReason = quitRequested ? "User Quit." : "Game Ended";

            _gameVisualService.RenderGameOver(console, Name, _gameState!.Score, 100, endReason, false);
        }

        private bool CheckCollision()
        {
            // Check if any ghost is at the same position as Pacman
            foreach(var ghost in _gameState!.Ghosts)
            {
                if (ghost.CurrentPosition == _gameState.PacManPosition) return true;
            }

            return false;
        }

        private void UpdateGhosts()
        {
            // TODO: Implement ghost AI logic (e.g., move towards Pacman or random)
            // Keep this method performant: avoid LINQ allocations if possible.
        }

        private void Render(IAnsiConsole console)
        {
            console.Clear();
            var table = new Table().Border(TableBorder.None);

            // FIX: Define columns based on the grid width
            for (int i = 0; i < _gameState!.Width; i++)
            {
                table.AddColumn("");
            }

            // Now adding rows will work because columns are defined
            for (int y = 0; y < _gameState.Height; y++)
            {
                var row = new List<string>();
                for (int x = 0; x < _gameState.Width; x++)
                {
                    if (x == _gameState.PacManPosition.X && y == _gameState.PacManPosition.Y)
                        row.Add("[yellow]C[/]");
                    else if (_gameState.Grid[y, x] == TileType.Wall) // Added check
                        row.Add("[blue]#[/]");
                    else if (_gameState.Grid[y, x] == TileType.Pellet) // Added check
                        row.Add(".");
                    else
                        row.Add(" ");
                }
                table.AddRow(row.ToArray());
            }
            console.Write(table);
        }

        private void UpdatePlayer(GameInput input)
        {
            var newPos = _gameState!.PacManPosition;

            // 1. Calculate potential new position
            switch(input)
            {
                case GameInput.W: newPos = newPos with { Y = Math.Max(0, newPos.Y - 1) }; break;
                case GameInput.S: newPos = newPos with { Y = Math.Min(_gameState.Height - 1, newPos.Y + 1) }; break;
                case GameInput.A: newPos = newPos with { X = Math.Max(0, newPos.X - 1) }; break;
                case GameInput.D: newPos = newPos with { X = Math.Min(_gameState.Width - 1, newPos.X + 1) }; break;
            }

            // 2. Boundary and Collision Check (assuming wall = 1)
            if (_gameState.Grid[newPos.Y, newPos.X] == TileType.Wall) return;

            // 3. Update position
            _gameState.PacManPosition = newPos;

            // 4. Consume Tile
            if(_gameState.TryConsumeTile(newPos, out int points))
            {
                _gameState.Score += points;
            }
        }

        private bool IsQuit(GameInput input) => input == GameInput.Escape;

        public async Task ShutdownAsync() => await Task.CompletedTask;
    }
}
