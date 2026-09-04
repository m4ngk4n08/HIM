using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<GameScoreService> _logger;
        private Dictionary<string, int> _scores = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public GameScoreService(IOptions<GameSettings> settings, ILogger<GameScoreService> logger)
        {
            _logger = logger;

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
                catch (Exception ex)
                {
                    // Falls back to an empty leaderboard either way - this is about making the
                    // failure visible, not changing what happens when the scores file is corrupt
                    // or unreadable.
                    _logger.LogWarning(ex, "Could not load game scores from {StoragePath}; starting with an empty leaderboard.", _storagePath);
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
            catch (Exception ex)
            {
                // A failed score write must never take down a visitor's game session - this is
                // added visibility, not added failure. The storage path is a local file path, not
                // network-derived input, so it does not go through SanitizeLogInput.
                _logger.LogWarning(ex, "Could not save game scores to {StoragePath}.", _storagePath);
            }
        }

        public Task<int> GetHighScoreAsync(string name)
        {
            lock (_lock)
            {
                return Task.FromResult(_scores.GetValueOrDefault(name, 0));
            }
        }

        public Task<IReadOnlyDictionary<string, int>> GetAllScoresAsync()
        {
            lock (_lock)
            {
                // A copy, not the live dictionary - callers must not be able to mutate the
                // store by holding onto what this hands back.
                return Task.FromResult<IReadOnlyDictionary<string, int>>(
                    new Dictionary<string, int>(_scores, StringComparer.OrdinalIgnoreCase));
            }
        }
    }
}
