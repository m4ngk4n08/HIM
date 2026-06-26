using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Game.TheGame
{
    public class CodeDebugger(
        IGameInputService gameInputService,
        IGameVisualService gameVisualService,
        IGameScoreService gameScoreService,
        ICommandDispatcherHelper commandDispatcherHelper,
        ITerminalLayoutService terminalLayoutService
        ) : IGameService
    {
        private readonly IGameVisualService _gameVisualService = gameVisualService;
        private readonly IGameScoreService _gameScoreService = gameScoreService;
        private readonly ICommandDispatcherHelper _commandDispatcherHelper = commandDispatcherHelper;
        private readonly ITerminalLayoutService _terminalLayoutService = terminalLayoutService;

        public string Name => "CodeDebugger";

        public string Description => "Perform a critical code review. Find the bug before the system crashes.";

        public async Task ExecuteAsync(IAnsiConsole console, Stream strea, CancellationToken ct)
        {
            await _terminalLayoutService.InitializeTerminalLayoutAsync(console, strea, ct);
            _gameVisualService.ApplyGameTheme(console);

            string instructions = "You will be shown snippets of broken C# code.\n" +
                                 "Identify the BUG!\n" +
                                 "Type [/q] to quit.";

            await _gameVisualService.ShowInstructionAsync(console, Name, instructions, ct);

            var challenges = GetChallenges();
            int score = 0;
            int maxScore = challenges.Count * 20;
            bool quitRequested = false;

            for ( int i = 0; i < challenges.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var challenge = challenges[i];

                console.WriteLine();
                console.MarkupLine($"[bold yellow]SITREP {i + 1}/{challenges.Count}[/] - {challenge.Scenario}");

                // Render the code snippet in a professional panel
                console.Write(new Panel(new Text(challenge.Code, new Style(Color.Gray)))
                    .Header("[bold red] CRITICAL BUG DETECTED [/]")
                    .BorderColor(Color.Red)
                    .Padding(1, 1)
                    );

                console.Write(new Text("Identify Bug > ", new Style(Color.Green)));

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

                if(challenge.Judge(trimmed))
                {
                    score += 20;
                    console.MarkupLine("[bold green] BUG FIXED! System stable. +20 XP [/]");
                }
                else
                {
                    console.MarkupLine("[red] FIX FAILED.[/]");
                    console.MarkupLine($"[grey]Correct Answer: {challenge.Answer}");
                }
                await Task.Delay(1000, ct);
            }

            int highScore = await _gameScoreService.GetHighScoreAsync(Name);
            bool isNewRecord = score > highScore;
            await _gameScoreService.SaveScoreAsync(Name, score);

            string endReason = quitRequested ? "User Quit" : "Audit Complete";
            _gameVisualService.RenderGameOver(console, Name, score, maxScore, endReason, isNewRecord);

        }
        private static IReadOnlyList<DebugChallenge> GetChallenges() => [
            new(
                     "A simple loop that should print 1 to 10.",
                     "for (int i = 1; i <= 10; i++)\n{\n    Console.WriteLine(i);\n}",
                     a => a.Contains("no bug", StringComparison.OrdinalIgnoreCase) || a.Contains("correct",
         StringComparison.OrdinalIgnoreCase),
                     "No bug here!"
                 ),
                 new(
                     "An attempt to access a list element.",
                     "var list = new List<int> { 1, 2, 3 };\nvar item = list[3];",
                     a => a.Contains("index", StringComparison.OrdinalIgnoreCase) || a.Contains("3", StringComparison.Ordinal),
                     "IndexOutOfRangeException: index 3 is out of range."
                 ),
                 new(
                     "A potential null crash.",
                     "string name = null;\nint length = name.Length;",
                     a => a.Contains("null", StringComparison.OrdinalIgnoreCase),
                     "NullReferenceException: 'name' is null."
                 ),
                 new(
                     "A loop that never ends.",
                     "while (true) { Console.WriteLine(\"Looping...\"); }",
                     a => a.Contains("infinite", StringComparison.OrdinalIgnoreCase),
                     "Infinite Loop."
                 ),
                 new(
                     "A dangerous cast.",
                     "object obj = \"Hello\";\nint val = (int)obj;",
                     a => a.Contains("cast", StringComparison.OrdinalIgnoreCase) || a.Contains("invalid",
         StringComparison.OrdinalIgnoreCase),
                     "InvalidCastException: Cannot cast string to int."
                 )
        ];

        private record DebugChallenge(string Scenario, string Code, Func<string, bool> Judge, string Answer);

        public Task InitializeAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task ShutdownAsync()
           => Task.CompletedTask;
    }
}
