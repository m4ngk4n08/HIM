using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace HIM.Gateway.Services.SSH.Game.TheGame
{
    public class RegexQuest(
        IGameInputService gameInputService,
        IGameVisualService gameVisualService,
        IGameScoreService gameScoreService,
        ICommandDispatcherHelper commandDispatcherHelper) : IGameService
    {
        private readonly IGameVisualService _gameVisualService = gameVisualService;
        private readonly IGameScoreService _gameScoreService = gameScoreService;
        private readonly ICommandDispatcherHelper _commandDispatcherHelper = commandDispatcherHelper;

        public string Name => "RegexQuest";

        public string Description => "Master the art of pattern matching. Write the regex to capture the target.";

        public async Task ExecuteAsync(IAnsiConsole console, Stream strea, CancellationToken ct)
        {
            _gameVisualService.ApplyGameTheme(console);
            
            string regexInstructions = "Objective: Write a Regular Expression (Regex) that matches the target string exactly.\n" +
                                   "Your regex will be tested against the target string. If it matches, you advance.\n" +
                                   "Type [/q] at any time to quit.";
            await _gameVisualService.ShowInstructionAsync(console, Name, regexInstructions, ct);

            await Task.Delay(1500, ct);

            var levels = GetLevels();
            int score = 0;
            int maxScore = levels.Count * 20;
            bool quitRequested = false;

            console.MarkupLine($"[bold cyan]Welcome to RegexQuest.[/]");
            console.MarkupLine("[grey]Objective: Write a regex that matcher the target string exactly.[/]");

            for ( int i = 0; i < levels.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var level = levels[i];

                console.WriteLine();
                console.MarkupLine($"[bold yellow]Level {i + 1}/{levels.Count}[/] - {level.Objective}");
                console.MarkupLine($"[grey]Target String:[/] [white]\"{level.Target}\"[/]");
                console.Write(new Text("Regex >", new Style(Color.Green)));

                string raw;
                try
                {
                    raw = await _commandDispatcherHelper.ReadInputManualAsync(console, strea, ct);
                }
                catch (OperationCanceledException) { return; }

                string trimmed = raw.Trim();
                if(trimmed.Equals("/q", StringComparison.OrdinalIgnoreCase))
                {
                    quitRequested = true;
                    break;
                }

                if(ValidateRegex(trimmed, level.Target))
                {
                    score += 20;
                    console.MarkupLine("[bold green]MATCH FOUND! +20 XP[/]");
                }
                else
                {
                    console.MarkupLine("[red]No Match.[/]");
                    console.MarkupLine($"[grey]Hint: Try focusing on {level.Hint}[/]");
                }

                await Task.Delay(800, ct);
            }

            int highScore = await _gameScoreService.GetHighScoreAsync(Name);
            bool isNewRecord = score > highScore;
            await _gameScoreService.SaveScoreAsync(Name, score);

            string endReasong = quitRequested ? "User Quit" : "Challenge Complete";
            _gameVisualService.RenderGameOver(console, Name, score, maxScore, endReasong, isNewRecord);

        }

        private bool ValidateRegex(string pattern, string target)
        {
            try
            {
                // We check if the user's regex matches the target string.
                // To prevent "cheating" (e.g. just typing '.+'), we could add
                // checks for specific anchors or characters, but for Tier 1,
                // a successful match is the goal.
                return Regex.IsMatch(target, pattern);
            }
            catch 
            {
                return false;
            }
        }
        private static IReadOnlyList<RegexLevel> GetLevels() => [
            new("Capture a simple email address", "user@example.com", "look for @ and ."),
            new("Match an IPv4 address", "192.168.1.1", "four groups of digits separated by dots"),
            new("Extract a date (YYYY-MM-DD)", "2026-06-24", "digits, hyphen, digits, hyphen, digits"),
            new("Match a strong password (1 uppercase, 1 digit, 8+ chars)", "Pass1234!", "lookahead for uppercase and digit"),
            new("Identify a hexadecimal color code", "#FF5733", "starts with # followed by 6 hex chars")
        ];

        private record RegexLevel(string Objective, string Target, string Hint);

        public Task InitializeAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task ShutdownAsync()
            => Task.CompletedTask;
    }
}
