using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.IGame
{
    public interface IGameFactoryService
    {

        IGameService? GetGame(string name);

        IEnumerable<(string name, string description)> GetAvailableGames();
    }
}
