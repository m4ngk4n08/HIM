using HIM.Gateway.Services.SSH.Interfaces.IGame;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Game
{
    internal sealed class GameFactoryService(IEnumerable<IGameService> games) : IGameFactoryService
    {
        /// <summary>
        /// Resolves a game implementation by its name.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<(string name, string description)> GetAvailableGames()
            => games.Select(g => (g.Name, g.Description));

        /// <summary>
        /// Get a list of all available games and their descriptions
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public IGameService? GetGame(string name)
            => games.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
