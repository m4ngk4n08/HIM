using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH.Commands
{
    // Task 26A, Phase 4 module: today a high score is only visible inside the game that set it
    // (GetHighScoreAsync has exactly three callers, all inside a game's own end screen). /scores
    // makes the leaderboard visible without playing.
    //
    // GameScoreService holds one all-time-best per game name, Dictionary<string,int> - no
    // per-player attribution, no timestamp, no history (GameScoreService.cs). This is the house
    // record for each game and nothing else - not "your best", not "recent scores". A game with
    // no entry at all has never been played, and GetHighScoreAsync would return 0 for that case
    // exactly as it would for a real score of zero, so this renders a dash instead of a number
    // for any game GetAllScoresAsync doesn't have an entry for.
    [SlashCommand("/scores", "See the house record for every game", HelpOrder = 11)]
    public sealed class ScoresCommand : ISlashCommand
    {
        private readonly IGameFactoryService _gameFactory;
        private readonly IGameScoreService _scoreService;
        private readonly IThemeService _theme;

        public ScoresCommand(IGameFactoryService gameFactory, IGameScoreService scoreService, IThemeService theme)
        {
            _gameFactory = gameFactory;
            _scoreService = scoreService;
            _theme = theme;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var console = context.Console;
            var scores = await _scoreService.GetAllScoresAsync();

            console.Write(new Rule("[bold]HIGH SCORES[/]").RuleStyle(_theme.PrimaryColor));

            var table = new Table().Border(TableBorder.Rounded).Title("[bold]SCORES[/]");
            table.AddColumn("Game").AddColumn("Description").AddColumn("Best");

            foreach (var (name, description) in _gameFactory.GetAvailableGames())
            {
                var best = scores.TryGetValue(name, out var score) ? score.ToString() : "—";
                table.AddRow(name.EscapeMarkup(), description.EscapeMarkup(), best);
            }

            console.Write(table);
        }
    }
}
