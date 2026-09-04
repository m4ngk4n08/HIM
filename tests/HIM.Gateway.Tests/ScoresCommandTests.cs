using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Commands;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 26A: /scores shows the leaderboard without playing. GameScoreService holds one all-time
/// best per game name and nothing else, so a never-played game must render as unplayed, not 0 -
/// GetHighScoreAsync returns 0 for both cases, which is exactly why ScoresCommand goes through
/// GetAllScoresAsync instead.
/// </summary>
public class ScoresCommandTests
{
    private sealed class FakeGameFactoryService : IGameFactoryService
    {
        private readonly List<(string name, string description)> _games;
        public FakeGameFactoryService(params (string name, string description)[] games) => _games = games.ToList();
        public IEnumerable<(string name, string description)> GetAvailableGames() => _games;
        public IGameService? GetGame(string name) => null;
    }

    private sealed class FakeGameScoreService : IGameScoreService
    {
        private readonly Dictionary<string, int> _scores;
        public FakeGameScoreService(Dictionary<string, int> scores) => _scores = scores;
        public Task SaveScoreAsync(string gameName, int score) => Task.CompletedTask;
        public Task<int> GetHighScoreAsync(string name) => Task.FromResult(_scores.GetValueOrDefault(name, 0));
        public Task<IReadOnlyDictionary<string, int>> GetAllScoresAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(_scores);
    }

    private static async Task<string> RunScoresAsync(FakeGameFactoryService factory, FakeGameScoreService scores)
    {
        var command = new ScoresCommand(factory, scores, new ThemeService());

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 240;
        using var stream = new MemoryStream();
        var context = new CommandContext(console, stream, "/scores", new PortfolioData(), "session", CancellationToken.None);

        await command.ExecuteAsync(context);
        return writer.ToString();
    }

    [Fact]
    public async Task EveryGameWithAScore_RendersItsBestScore()
    {
        var factory = new FakeGameFactoryService(("Trivia", "Test your technical knowledge."));
        var scores = new FakeGameScoreService(new Dictionary<string, int> { ["Trivia"] = 42 });

        var output = await RunScoresAsync(factory, scores);

        Assert.Contains("Trivia", output);
        Assert.Contains("42", output);
    }

    [Fact]
    public async Task NeverPlayedGame_RendersAsUnplayed_NotZero()
    {
        var factory = new FakeGameFactoryService(("Pac-Man", "Classic ASCII Pac-Man maze navigation."));
        var scores = new FakeGameScoreService(new Dictionary<string, int>());

        var output = await RunScoresAsync(factory, scores);

        Assert.Contains("Pac-Man", output);
        Assert.Contains("—", output);
        Assert.DoesNotContain("0", output);
    }
}
