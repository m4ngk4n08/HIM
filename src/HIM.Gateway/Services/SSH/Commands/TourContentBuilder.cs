using HIM.Gateway.Extensions;
using HIM.Gateway.Models.Knowledge;
using Spectre.Console;
using System.Collections.Generic;
using System.Linq;

namespace HIM.Gateway.Services.SSH.Commands
{
    /// <summary>
    /// One tour step: a title and the markup-ready lines to render for it. Lines are already
    /// safe to hand to console.MarkupLine - see TourContentBuilder.Safe.
    /// </summary>
    internal sealed record TourStep(string Title, IReadOnlyList<string> Lines);

    /// <summary>
    /// Task 26C: builds the three /tour modes' step lists from the PortfolioData the command
    /// already receives on CommandContext.Data - the same object /menu reads from, not a new
    /// source of truth.
    ///
    /// Security: PortfolioData is owner-authored, not network input, so SanitizeLogInput is not
    /// the concern here. But PersonalInfo carries contact details, and Task 21D established that
    /// everything rendered to a visitor passes SanitizerExtension.RedactPhone before
    /// EscapeMarkup, the same egress boundary /menu, /cite, /defense and /who already follow. A
    /// contact step that skipped this would be the first regression of that rule, so every line
    /// this class hands back has already gone through Safe() - callers must not re-escape it.
    /// </summary>
    internal static class TourContentBuilder
    {
        public static IReadOnlyList<TourStep> BuildSteps(TourMode mode, PortfolioData data) => mode switch
        {
            TourMode.Recruiter => new[]
            {
                BuildExperienceStep(data),
                BuildSkillsStep(data),
                BuildProjectsStep(data),
                BuildContactStep(data, closing: false)
            },
            TourMode.Engineer => new[]
            {
                BuildArchitectureStep(),
                BuildProjectsStep(data),
                BuildSkillsStep(data),
                BuildRagStep(),
                BuildContactStep(data, closing: false)
            },
            _ => new[]
            {
                BuildWelcomeStep(data),
                BuildSkillsStep(data),
                BuildExperienceStep(data),
                BuildProjectsStep(data),
                BuildContactStep(data, closing: true)
            }
        };

        private static TourStep BuildWelcomeStep(PortfolioData data)
        {
            var p = data.PersonalInfo;
            var lines = new List<string>
            {
                $"[cyan1]{Safe($"{p.Name} — {p.Role}")}[/]",
                Safe(p.Summary)
            };
            return new TourStep("WELCOME", lines);
        }

        private static TourStep BuildSkillsStep(PortfolioData data)
        {
            var lines = data.TechnicalSkills
                .Select(category => $"[yellow]{Safe(CapitalizeFirst(category.Key))}[/]: {Safe(string.Join(", ", category.Value))}")
                .ToList();

            if (lines.Count == 0) lines.Add("[grey]No skills on file.[/]");
            return new TourStep("SKILLS & STACK", lines);
        }

        private static TourStep BuildExperienceStep(PortfolioData data)
        {
            var lines = new List<string>();
            foreach (var job in data.Experiences)
            {
                lines.Add($"[bold cyan]{Safe($"{job.Position} @ {job.Company}")}[/] [grey]({Safe(job.Duration)})[/]");
                foreach (var highlight in job.Highlights)
                {
                    lines.Add($"  • {Safe(highlight)}");
                }
            }

            if (lines.Count == 0) lines.Add("[grey]No experience on file.[/]");
            return new TourStep("EXPERIENCE", lines);
        }

        private static TourStep BuildProjectsStep(PortfolioData data)
        {
            var projects = data.Projects ?? new List<ProjectItem>();
            var lines = projects
                .Select(proj => $"[bold]{Safe(proj.Name)}[/] — {Safe(proj.Stack)} [green]({Safe(proj.Status)})[/]")
                .ToList();

            if (lines.Count == 0) lines.Add("[grey]No projects on file.[/]");
            return new TourStep("PROJECTS", lines);
        }

        private static TourStep BuildArchitectureStep()
        {
            var lines = new List<string>
            {
                Safe("Two .NET 10 services, deployed as containers to a single VPS."),
                Safe("HIM.Gateway: a custom SSH server (Microsoft.DevTunnels.Ssh) fronting a Spectre.Console TUI - the accept-loop defense pipeline, session lifecycle and command dispatch all live here."),
                Safe("HIM.AiService: an ASP.NET Core service bound to 127.0.0.1:8080, never exposed publicly - the gateway reaches it over the compose bridge.")
            };
            return new TourStep("ARCHITECTURE", lines);
        }

        private static TourStep BuildRagStep()
        {
            var lines = new List<string>
            {
                Safe("Retrieval is manual, not a vector-DB SaaS: in-process ONNX all-minilm-l6-v2 embeddings, no external embedding call."),
                Safe("Search is SIMD vector search over the embedded knowledge base."),
                Safe("Chat generation goes to Gemini once retrieval narrows down the relevant chunks.")
            };
            return new TourStep("THE AI / RAG PIPELINE", lines);
        }

        private static TourStep BuildContactStep(PortfolioData data, bool closing)
        {
            var contact = data.PersonalInfo.Contact ?? new Dictionary<string, string>();
            var lines = contact
                .Select(kv => $"[grey]{Safe(CapitalizeFirst(kv.Key))}:[/] {Safe(kv.Value)}")
                .ToList();

            if (lines.Count == 0) lines.Add("[grey]No contact details on file.[/]");

            if (closing)
            {
                lines.Add(string.Empty);
                lines.Add("[grey]Keep exploring: /menu, /game, /scores, or just ask a question.[/]");
            }

            return new TourStep(closing ? "WRAP-UP" : "CONTACT", lines);
        }

        private static string CapitalizeFirst(string value) =>
            value.Length == 0 ? value : char.ToUpper(value[0]) + value[1..];

        // The one place every rendered tour string passes through before it reaches a caller:
        // RedactPhone then EscapeMarkup, in that order, same as WhoCommand.cs:48.
        private static string Safe(string? value) =>
            SanitizerExtension.RedactPhone(value ?? string.Empty).EscapeMarkup();
    }
}
