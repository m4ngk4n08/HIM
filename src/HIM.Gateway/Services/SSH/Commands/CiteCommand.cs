using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH.Commands
{
    // Task 22C, module 03 from the rebuild artifact ("ask --cite"). Built as a registry command
    // rather than a flag on chat - there is no "ask" command or flag grammar in this TUI, chat is
    // just typing a question, and adding one for a single feature isn't worth a new input grammar.
    // Explains the *last* question asked in this session: ask something, then type /cite.
    [SlashCommand("/cite", "Show which knowledge-base chunks answered your last question", HelpOrder = 8)]
    public sealed class CiteCommand : ISlashCommand
    {
        // Roughly 100 characters - legibility, not a knowledge-base dump. The AI service already
        // caps its own preview at 150; this trims further for the terminal specifically.
        private const int PreviewMaxLength = 100;

        private readonly IAiClientService _aiClientService;
        private readonly UserSessionState _sessionState;

        public CiteCommand(IAiClientService aiClientService, UserSessionState sessionState)
        {
            _aiClientService = aiClientService;
            _sessionState = sessionState;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var console = context.Console;
            var lastQuestion = _sessionState.LastQuestion;

            if (string.IsNullOrWhiteSpace(lastQuestion))
            {
                console.MarkupLine("[grey]Ask something first, then type /cite to see where the answer came from.[/]");
                return;
            }

            // No AI query budget or cooldown check here on purpose - this makes no model call,
            // it only re-runs retrieval against the question already asked.
            var (result, error) = await _aiClientService.GetCitationsAsync(lastQuestion, context.Ct, context.SessionId);

            if (error != null)
            {
                // The error text came back from the AI service over the wire - free text, same
                // egress boundary as everything else this command renders.
                console.MarkupLine($"[red]Couldn't retrieve citations: {SanitizerExtension.RedactPhone(error).EscapeMarkup()}[/]");
                return;
            }

            if (result == null || result.Chunks.Count == 0)
            {
                console.MarkupLine("[grey]Nothing in the knowledge base cleared the relevance cutoff for that question.[/]");
            }
            else
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title("[bold cyan]SOURCES[/]");
                table.AddColumn("Source").AddColumn("Score").AddColumn("Preview");

                foreach (var chunk in result.Chunks)
                {
                    // Every rendered string - source label and preview alike - is free text out
                    // of the knowledge base, so both go through the same redaction boundary
                    // MenuCommandService uses (Task 21D/BL-8): a future free-text field is
                    // unprotected by default unless it's routed through RedactPhone explicitly.
                    var label = SanitizerExtension.RedactPhone(chunk.Label).EscapeMarkup();
                    var preview = SanitizerExtension.RedactPhone(TrimPreview(chunk.Preview)).EscapeMarkup();
                    table.AddRow(label, chunk.Score.ToString("F3"), preview);
                }

                console.Write(table);
            }

            var timings = result?.Timings;
            if (timings != null)
            {
                console.MarkupLine(
                    $"[grey]{timings.ChunksScanned} chunks scanned in {timings.SearchMs:F3} ms " +
                    $"({timings.ChunksReturned} above cutoff) — embedding took {timings.EmbeddingMs:F1} ms.[/]");
            }
        }

        private static string TrimPreview(string preview) =>
            preview.Length > PreviewMaxLength ? preview[..PreviewMaxLength] + "…" : preview;
    }
}
