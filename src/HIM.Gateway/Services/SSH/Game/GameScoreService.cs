using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Microsoft.Extensions.Options;
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

        public GameScoreService(IOptions<GameSettings> settings)
        {
            // A relative path is resolved against the app's own directory, not the current
            // working directory - matching PortfolioDataProvider and StatsCommandService. An
            // absolute path (what the container sets via GameSettings__ScoresPath) is used as
            // given.
            var configuredPath = settings.Value.ScoresPath;
            _storagePath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath);
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
