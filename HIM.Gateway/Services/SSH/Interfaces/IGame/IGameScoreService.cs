using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.IGame
{
    public interface IGameScoreService
    {
        Task SaveScoreAsync(string gameName, int score);

        Task<int> GetHighScoreAsync(string name);
    }
}
