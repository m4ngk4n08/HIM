using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH.Commands
{
    // Task 26D, Phase 4 module "tour": a guided walkthrough of the portfolio, three audience
    // modes from plans/plan-tour.md and plans/guide-plan-tour.md - kept for content, not design
    // (see TourContentBuilder and TourState). Navigation reads a line via
    // ICommandDispatcherHelper.ReadInputManualAsync, the same nested-prompt reader /menu uses -
    // it takes context.Ct, not ConsoleEngineService's per-iteration idle-timeout token, so (as
    // for /menu and /game already) a visitor sitting on a step is never disconnected for
    // inactivity while a tour is open. See InputBufferRaceTests/TourCommandTests for the
    // regression coverage of that guarantee.
    [SlashCommand("/tour", "Guided walkthrough of the portfolio", Usage = "/tour [quick|recruiter|engineer]", HelpOrder = 12)]
    public sealed class TourCommand : ISlashCommand
    {
        private readonly ICommandDispatcherHelper _dispatcher;
        private readonly TourState _tourState;
        private readonly IThemeService _theme;

        public TourCommand(ICommandDispatcherHelper dispatcher, TourState tourState, IThemeService theme)
        {
            _dispatcher = dispatcher;
            _tourState = tourState;
            _theme = theme;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var console = context.Console;
            var (mode, hint) = ParseMode(context.RawCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (hint != null)
            {
                console.MarkupLine(hint);
            }

            var steps = TourContentBuilder.BuildSteps(mode, context.Data);

            _tourState.Mode = mode;
            _tourState.IsActive = true;
            _tourState.CurrentStepIndex = 0;

            try
            {
                while (_tourState.CurrentStepIndex < steps.Count && !context.Ct.IsCancellationRequested)
                {
                    RenderStep(console, steps, _tourState.CurrentStepIndex);

                    var input = (await _dispatcher.ReadInputManualAsync(console, context.Stream, context.Ct))
                        .Trim()
                        .ToLowerInvariant();

                    if (input.Length == 0 || input == "next")
                    {
                        _tourState.CurrentStepIndex++;
                    }
                    else if (input == "back")
                    {
                        if (_tourState.CurrentStepIndex > 0)
                        {
                            _tourState.CurrentStepIndex--;
                        }
                    }
                    else if (input == "exit" || input == "q")
                    {
                        break;
                    }
                    else if (int.TryParse(input, out var stepNumber) && stepNumber >= 1 && stepNumber <= steps.Count)
                    {
                        _tourState.CurrentStepIndex = stepNumber - 1;
                    }
                    else
                    {
                        console.MarkupLine("[grey]Didn't catch that - next, back, exit, or a step number.[/]");
                    }
                }
            }
            finally
            {
                // Cleared on every exit path (natural end, exit/q, or cancellation) so a later
                // /tour always starts fresh rather than resuming a stale position.
                _tourState.IsActive = false;
            }

            console.MarkupLine("[grey]Tour ended.[/]");
        }

        private void RenderStep(IAnsiConsole console, IReadOnlyList<TourStep> steps, int index)
        {
            var step = steps[index];
            console.Write(new Rule($"[bold]{step.Title}[/]").RuleStyle(_theme.PrimaryColor));
            foreach (var line in step.Lines)
            {
                console.MarkupLine(line);
            }
            console.WriteLine();
            console.MarkupLine($"[grey]Step {index + 1}/{steps.Count} — next, back, exit, or a step number[/]");
        }

        private static (TourMode Mode, string? Hint) ParseMode(string[] parts)
        {
            if (parts.Length < 2)
            {
                return (TourMode.Quick, null);
            }

            return parts[1].ToLowerInvariant() switch
            {
                "quick" => (TourMode.Quick, null),
                "recruiter" => (TourMode.Recruiter, null),
                "engineer" => (TourMode.Engineer, null),
                _ => (TourMode.Quick, "[grey]Unknown tour mode - showing the quick tour. Usage: /tour [[quick|recruiter|engineer]][/]")
            };
        }
    }
}
