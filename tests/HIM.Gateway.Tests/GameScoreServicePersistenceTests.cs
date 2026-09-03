using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Game;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 22A: the leaderboard used to live at a hardcoded path inside the container's writable
/// layer, so it was wiped on every deploy. GameScoreService now takes its storage path from
/// GameSettings via IOptions, so a configured path (the compose mount in production) survives a
/// fresh instance the way a redeploy recreates the process.
/// Task 23B: a failed save or load is now logged instead of silently swallowed.
/// </summary>
public class GameScoreServicePersistenceTests
{
    // No existing capturing ILogger fake was found in this test project (only NullLogger-shaped
    // stand-ins that discard every call) - this one records level+exception so a test can assert
    // a failure was actually logged, not just that the call didn't throw.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task ScoreSavedAtConfiguredPath_IsReadBackByAFreshServiceInstance()
    {
        var scoresPath = Path.Combine(Path.GetTempPath(), $"him-test-scores-{Guid.NewGuid()}.json");
        var settings = Options.Create(new GameSettings { ScoresPath = scoresPath });

        try
        {
            var writer = new GameScoreService(settings, new CapturingLogger<GameScoreService>());
            await writer.SaveScoreAsync("Trivia", 42);

            // Pins the actual location, not just round-tripping through the same service field -
            // two instances sharing the same (wrong, hardcoded) path would still agree with each
            // other while writing nowhere near the configured one.
            Assert.True(File.Exists(scoresPath));

            // A fresh instance simulates a redeploy: nothing in-process survives, only what was
            // written to the configured path.
            var reader = new GameScoreService(settings, new CapturingLogger<GameScoreService>());
            var score = await reader.GetHighScoreAsync("Trivia");

            Assert.Equal(42, score);
        }
        finally
        {
            if (File.Exists(scoresPath)) File.Delete(scoresPath);
        }
    }

    [Fact]
    public async Task SaveScore_ToAnUnwritablePath_LogsAWarning_AndDoesNotThrow()
    {
        // A path under a file (not a directory that doesn't exist) fails deterministically on
        // both Windows and Linux: the OS refuses to create a file where a path segment is itself
        // a regular file, whereas "directory that doesn't exist" behaves inconsistently only on
        // this one axis - CreateDirectory-then-write patterns could paper over it, a raw
        // File.WriteAllText will not, so this exercises the real failure mode either way.
        var blockingFile = Path.Combine(Path.GetTempPath(), $"him-test-blocker-{Guid.NewGuid()}.json");
        File.WriteAllText(blockingFile, "not a directory");
        var scoresPath = Path.Combine(blockingFile, "scores.json");
        var settings = Options.Create(new GameSettings { ScoresPath = scoresPath });
        var logger = new CapturingLogger<GameScoreService>();

        try
        {
            var service = new GameScoreService(settings, logger);

            await service.SaveScoreAsync("Trivia", 10);

            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Exception != null);
        }
        finally
        {
            File.Delete(blockingFile);
        }
    }

    [Fact]
    public void LoadScores_FromACorruptFile_LogsAWarning_AndFallsBackToEmpty()
    {
        var scoresPath = Path.Combine(Path.GetTempPath(), $"him-test-corrupt-{Guid.NewGuid()}.json");
        File.WriteAllText(scoresPath, "{ not valid json");
        var settings = Options.Create(new GameSettings { ScoresPath = scoresPath });
        var logger = new CapturingLogger<GameScoreService>();

        try
        {
            var service = new GameScoreService(settings, logger);

            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Exception != null);
        }
        finally
        {
            File.Delete(scoresPath);
        }
    }
}
