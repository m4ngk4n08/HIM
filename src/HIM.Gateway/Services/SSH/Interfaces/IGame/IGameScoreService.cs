using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.IGame
{
    public interface IGameScoreService
    {
        Task SaveScoreAsync(string gameName, int score);

        Task<int> GetHighScoreAsync(string name);

        // Task 26A: /scores needs to tell "never played" apart from "scored zero", which
        // GetHighScoreAsync can't - it returns 0 for both. A snapshot of every entry that
        // actually exists in the store lets the caller decide what an absent game means.
        Task<IReadOnlyDictionary<string, int>> GetAllScoresAsync();
    }
}
