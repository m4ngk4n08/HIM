using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Game;
using Microsoft.Extensions.Options;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 22A: the leaderboard used to live at a hardcoded path inside the container's writable
/// layer, so it was wiped on every deploy. GameScoreService now takes its storage path from
/// GameSettings via IOptions, so a configured path (the compose mount in production) survives a
/// fresh instance the way a redeploy recreates the process.
/// </summary>
public class GameScoreServicePersistenceTests
{
    [Fact]
    public async Task ScoreSavedAtConfiguredPath_IsReadBackByAFreshServiceInstance()
    {
        var scoresPath = Path.Combine(Path.GetTempPath(), $"him-test-scores-{Guid.NewGuid()}.json");
        var settings = Options.Create(new GameSettings { ScoresPath = scoresPath });

        try
        {
            var writer = new GameScoreService(settings);
            await writer.SaveScoreAsync("Trivia", 42);

            // Pins the actual location, not just round-tripping through the same service field -
            // two instances sharing the same (wrong, hardcoded) path would still agree with each
            // other while writing nowhere near the configured one.
            Assert.True(File.Exists(scoresPath));

            // A fresh instance simulates a redeploy: nothing in-process survives, only what was
            // written to the configured path.
            var reader = new GameScoreService(settings);
            var score = await reader.GetHighScoreAsync("Trivia");

            Assert.Equal(42, score);
        }
        finally
        {
            if (File.Exists(scoresPath)) File.Delete(scoresPath);
        }
    }
}
