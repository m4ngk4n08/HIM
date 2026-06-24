using HIM.Gateway.Services.SSH.Interfaces.IGame;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace HIM.Gateway.Services.SSH.Game
{
    internal sealed class GameScoreService : IGameScoreService
    {
        private readonly string _storagePath;
        private Dictionary<string, int> _scores = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public GameScoreService()
        {
            // Stores score in the gateway's execution directory (or a configured path)
            _storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game-scores.json");
            LoadScores();
        }

        private void LoadScores()
        {
            if (File.Exists(_storagePath))
            {
                try
                {
                    var json = File.ReadAllText(_storagePath);
                    _scores = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();
                }
                catch 
                {
                    _scores = new();
                }
            }
        }

        public Task SaveScoreAsync(string gameName, int score)
        {
            lock (_lock)
            {
                if(!_scores.TryGetValue(gameName, out var currentHigh) || score > currentHigh)
                {
                    _scores[gameName] = score;
                    SaveScores();
                }
            }

            return Task.CompletedTask;
        }

        private void SaveScores()
        {
            try
            {
                var json = JsonSerializer.Serialize(_scores, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
            }
            catch 
            {
                // TODO:
                // Implement ILogger to report failuer to persist score
            }
        }

        public Task<int> GetHighScoreAsync(string name)
        {
            lock (_lock)
            {
                return Task.FromResult(_scores.GetValueOrDefault(name, 0));
            }
        }
    }
}
