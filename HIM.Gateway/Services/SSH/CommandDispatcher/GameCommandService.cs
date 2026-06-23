using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    internal sealed class GameCommandService : IGameCommandService
    {
        private sealed record TrivialQuestion(
            string Prompt,
            Func<string, bool> Judge,
            string Explanation
            );

        private static readonly Dictionary<string, string> EasterEggs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["rm -rf /"] = "Nice try. This is a sandboxed SSH Session.",
                ["sudo"] = "sudo: permission denied. You are 'explorer', not root",
                ["hire me"] = "DM recieved. Angelo will be in touch",
                ["42"] = "Corrent int the cosmic sense. But not for this question.",
                ["blockchain"] = "No. Just.... no.",
                ["hello world"] = "A classic. Still wrong though.",
            };

        public async Task ExecuteAsync(IAnsiConsole console, CancellationToken ct)
        {
            RenderHeader(console);

            var questions = BuildQuestion();
            int score = 0;
            int maxScore = questions.Count * 10;

            for ( int i = 0; i < questions.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var q = questions[i];
                console.WriteLine();
                console.MarkupLine($"[bold yellow]Q{i + 1}/{questions.Count}[/] {q.Prompt}");

                string raw;
                try
                {
                    raw = await new TextPrompt<string>("[grey]>[/]")
                        .AllowEmpty()
                        .ShowAsync(console, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                string trimmed = raw.Trim();

                if(EasterEggs.TryGetValue(trimmed, out var egg))
                {
                    console.MarkupLine($"[italic grey]{Markup.Escape(egg)}[/]");
                    await Task.Delay(1500, ct);
                    continue;
                }

                if (q.Judge(trimmed))
                {
                    score += 10;
                    console.MarkupLine("[bold green] Correct! +10 XP[/]");

                }
                else
                {
                    console.MarkupLine("[red] Not quite.[/]");
                    console.MarkupLine($"grey {Markup.Escape(q.Explanation)}[/]");
                }

                await Task.Delay(900, ct);

            }

            RenderFinalScore(console, score, maxScore);
        }

        private void RenderFinalScore(IAnsiConsole console, int score, int maxScore)
        {
            console.WriteLine();
            console.Write(new Rule("[bold cyan]RESULTS[/]").RuleStyle("cyan"));

            var rank = score switch
            {
                >= 50 => "[bold gold1]RANK: PRINCIPAL ENGINEER[/]",
                >= 40 => "[bold cyan]RANK: SENIOR DEV[/}",
                >= 30 => "[bold green]RANK: MID-LEVEL DEV[/]",
                >= 20 => "[bold yellow]RANK: JUNIOR DEV[/]",
                _ => "[bold red]RANK: INTERN[/]"
            };

            var grid = new Grid()
                .AddColumn().AddColumn()
                .AddRow("[grey]Score    :[/]", $"[white]{score} / {maxScore}[/]")
                .AddRow("[grey]Rank:    :[/]", rank);

            console.Write(new Panel(grid)
                .Header("[bold cyan] GAME OVER [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1));

            console.WriteLine();

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
                    || a.Contains("declar",   StringComparison.OrdinalIgnoreCase)
                    || a.Contains("metadata", StringComparison.OrdinalIgnoreCase),
                  "EXPOSE is a documentation hint — it declares the port but does NOT publish it. Use -p to publish."),
      ];

        private void RenderHeader(IAnsiConsole console)
        {
            console.WriteLine();
            console.Write(new Rule("[bold yellow]DEV TRIVIA[/]").RuleStyle("yellow"));
            console.MarkupLine("[gery]5 questions. Honest answers only.[/]");
            console.WriteLine();
        }
    }
}
