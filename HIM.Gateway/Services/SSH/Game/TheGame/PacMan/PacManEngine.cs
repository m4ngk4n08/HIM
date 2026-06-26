using System;

namespace HIM.Gateway.Services.SSH.Game.TheGame.PacMan
{
    // High-performance, memory-efficient representation of the maze elements.
    public enum TileType : byte
    {
        Empty = 0,
        Wall = 1,
        Pellet = 2,
        PowerPellet = 3,
        PacMan = 4,
        Ghost = 5
    }

    // Using a struct for lightweight state representation to reduce GC pressure.
    public readonly record struct Position(int X, int Y);

    // Encapsulates the game state to ensure thread-safety and predictability.
    public sealed class GameState
    {
        public int Width { get; }
        public int Height { get; }
        public TileType[,] Grid { get; }
        public Position PacManPosition { get; set; }
        public int Score { get; set; }
        public bool IsGameOver { get; set; }
        public int Lives { get; set; } = 3;

        public List<Ghost> Ghosts { get; set; } = new();
        public struct Ghost
        {
            public Position CurrentPosition { get; set; }
            public Position LastPosition { get; set; }
        }

        public void ResetPositions()
        {
            PacManPosition = new Position(5, 5); // Define spawn logic here
            // Reset ghost positions here too
        }

        public void UpdateGhost()
        {
            foreach (var ghost in Ghosts)
            {

            }
        }

        public GameState(int width, int height)
        {
            Width = width;
            Height = height;
            Grid = new TileType[height, width];
        }


        public bool TryConsumeTile(Position pos, out int points)
        {
            points = 0;
            var tile = Grid[pos.Y, pos.X];
        
            if(tile == TileType.Pellet)
            {
                points = 10;
                Grid[pos.Y, pos.X] = TileType.Empty;
                return true;
            }
            else if (tile == TileType.PowerPellet)
            {
                points = 50;
                Grid[pos.Y, pos.X] = TileType.Empty;
                return true;
            }

            return false;
        }

    }

}
