using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Game.TheGame
{
    public class TriviaGame(
        IGameInputService gameInputService,
        IGameVisualService gameVisualService,
        IGameScoreService gameScoreService,
        ICommandDispatcherHelper commandDispatcherHelper,
        ITerminalLayoutService terminalLayoutService
        ) : IGameService
    {
        private readonly IGameInputService _gameInputService = gameInputService;
        private readonly IGameVisualService _gameVisualService = gameVisualService;
        private readonly IGameScoreService _gameScoreService = gameScoreService;
        private readonly ICommandDispatcherHelper _commandDispatcherHelper = commandDispatcherHelper;
        private readonly ITerminalLayoutService _terminalLayoutService = terminalLayoutService;

        private record TrivialQuestion(string Prompt, Func<string, bool> Judge, string Explanation);

        public string Name => "Trivia";

        public string Description => "Test your technical knowledge with 5 random questions.";

        public async Task ExecuteAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            await _terminalLayoutService.InitializeTerminalLayoutAsync(console, stream, ct);
            _gameVisualService.ApplyGameTheme(console);
            
            string triviaInstructions = "You will be presented with 5 random technical questions.\n" +
                            "Type your answer and press Enter to submit.\n" +
                            "Correct answers earn 10 XP. Type [/q] at any time to quit.";
            await _gameVisualService.ShowInstructionAsync(console, Name, triviaInstructions, ct);

            var questions = GetRandomQuestions(5);
            int score = 0;
            int maxScore = questions.Count * 10;
            bool quitRequested = false;

            console.MarkupLine("[grey] /q to quit[/]");

            for (int i = 0; i < questions.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var q = questions[i];
                console.WriteLine();
                console.MarkupLine($"[bold yellow]Q{i + 1}/{questions.Count}[/] {q.Prompt}");
                console.Write(new Text("> ", new Style(Color.Green)));

                string raw;
                try
                {
                    raw = await _commandDispatcherHelper.ReadInputManualAsync(console, stream, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                string trimmed = raw.Trim();

                // -- EXIT COMMAND --
                if(trimmed.Equals("/q", StringComparison.OrdinalIgnoreCase))
                {
                    quitRequested = true;
                    break;
                }

                if (q.Judge(trimmed))
                {
                    score += 10;
                    console.MarkupLine("[bold green] Correct! +10 XP[/]");

                }
                else
                {
                    console.MarkupLine("[red] Not quite.[/]");
                    console.MarkupLine($"[grey] {Markup.Escape(q.Explanation)}[/]");
                }

                await Task.Delay(900, ct);
            }

            // Determine if user beat the high score before saving
            int highScore = await _gameScoreService.GetHighScoreAsync(Name);
            bool isNewRecord = score > highScore;

            await _gameScoreService.SaveScoreAsync(Name, score);

            string endReasong = quitRequested ? "User Quit" : "Session Ended";
            _gameVisualService.RenderGameOver(console, Name, score, maxScore, endReasong, isNewRecord);
        }

        private static IReadOnlyList<TrivialQuestion> GetRandomQuestions(int count)
        {
            var allQuestions = BuildQuestion();
            var rng = new Random();
            return allQuestions.OrderBy(x => rng.Next()).Take(count).ToList();
        }

        private static IReadOnlyList<TrivialQuestion> BuildQuestion() =>
           [
            new("What does the `async` keyword in C# enable?",
                        a => a.Contains("non-block", StringComparison.OrdinalIgnoreCase)
                        || a.Contains("asynchron", StringComparison.OrdinalIgnoreCase),
                        "It enables non-blocking execution via the Task-based async pattern."),

            new("What HTTP status code means 'resource not found'?",
                a => a.Contains("404", StringComparison.Ordinal),
                "404 Not Found."),

            new("What does SOLID stand for in software design? (name any one principle)",
                a => a.Contains("single", StringComparison.OrdinalIgnoreCase)
                || a.Contains("open",   StringComparison.OrdinalIgnoreCase)
                || a.Contains("liskov", StringComparison.OrdinalIgnoreCase)
                || a.Contains("interface", StringComparison.OrdinalIgnoreCase)
                || a.Contains("dependency", StringComparison.OrdinalIgnoreCase),
                "Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion."),

            new("What is the time complexity of a binary search?",
                a => a.Contains("log", StringComparison.OrdinalIgnoreCase)
                || a.Contains("O(log", StringComparison.OrdinalIgnoreCase),
                "O(log n) — halves the search space on each step."),

            new("In Docker, what does the EXPOSE instruction do?",
                a => a.Contains("document", StringComparison.OrdinalIgnoreCase)
                || a.Contains("declare",   StringComparison.OrdinalIgnoreCase)
                || a.Contains("metadata", StringComparison.OrdinalIgnoreCase),
                "EXPOSE is a documentation hint — it declares the port but does NOT publish it. Use -p to publish."),

            new("Which Git command is used to create a new branch and switch to it immediately?",
                a => a.Contains("checkout", StringComparison.OrdinalIgnoreCase)
                || a.Contains("switch", StringComparison.OrdinalIgnoreCase),
                "git checkout -b <name> or git switch -c <name>"),

            new("In JavaScript, what is the difference between `==` and `===`?",
                a => a.Contains("type", StringComparison.OrdinalIgnoreCase)
                || a.Contains("strict", StringComparison.OrdinalIgnoreCase),
                "=== performs strict equality check (checks both value and type), while == performs type coercion."),

            new("What is the primary purpose of a Load Balancer?",
                a => a.Contains("distribute", StringComparison.OrdinalIgnoreCase)
                || a.Contains("traffic", StringComparison.OrdinalIgnoreCase),
                "To distribute incoming network traffic across multiple servers."),

            new("What does 'DNS' stand for?",
                a => a.Contains("domain", StringComparison.OrdinalIgnoreCase)
                && a.Contains("name", StringComparison.OrdinalIgnoreCase),
                "Domain Name System."),

            new("In React, what is the purpose of 'hooks'?",
                a => a.Contains("function", StringComparison.OrdinalIgnoreCase)
                || a.Contains("state", StringComparison.OrdinalIgnoreCase),
                "Hooks allow you to use state and other React features in functional components."),

            new("What does 'CI/CD' stand for?",
                a => a.Contains("continuous", StringComparison.OrdinalIgnoreCase)
                && a.Contains("integration", StringComparison.OrdinalIgnoreCase)
                && a.Contains("delivery", StringComparison.OrdinalIgnoreCase)
                || a.Contains("deployment", StringComparison.OrdinalIgnoreCase),
                "Continuous Integration and Continuous Delivery (or Deployment)."),

            new("In SQL, which command is used to remove all records from a table without deleting the table itself?",
                a => a.Contains("truncate", StringComparison.OrdinalIgnoreCase),
                "TRUNCATE TABLE"),
            ];

        public Task InitializeAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task ShutdownAsync() => Task.CompletedTask;
    }
}
