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
    [SlashCommand("/cite", "Show which knowledge-base chunks answered your last question", Usage = "/cite [n]", HelpOrder = 8)]
    public sealed class CiteCommand : ISlashCommand
    {
        // Task 27B: the one and only trim, applied once to the full text the AI service now
        // sends - not to an already-truncated Preview. Roughly 100 characters: legibility, not a
        // knowledge-base dump.
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
            CitationResult? result;
            var cached = _sessionState.CachedCitation;
            if (cached != null && cached.Question == lastQuestion)
            {
                result = cached.Result;
            }
            else
            {
                string? error;
                (result, error) = await _aiClientService.GetCitationsAsync(lastQuestion, context.Ct, context.SessionId);

                if (error != null)
                {
                    // The error text came back from the AI service over the wire - free text, same
                    // egress boundary as everything else this command renders. Not cached - a
                    // transient failure must not stick for the rest of the session.
                    console.MarkupLine($"[red]Couldn't retrieve citations: {SanitizerExtension.RedactPhone(error).EscapeMarkup()}[/]");
                    return;
                }

                if (result != null)
                {
                    _sessionState.CachedCitation = new CachedCitation(lastQuestion, result);
                }
            }

            if (result == null || result.Chunks.Count == 0)
            {
                console.MarkupLine("[grey]Nothing in the knowledge base cleared the relevance cutoff for that question.[/]");
            }
            else
            {
                // Task 27C: argument parsing follows ThemeCommand exactly - split the raw command
                // on spaces, [1] is the argument if there is one. A bad or out-of-range number is
                // a hint, not an error - it must fall through to the table, never throw, and must
                // never touch CachedCitation (already untouched here; this method only reads it
                // above). A valid index renders that source alone and returns, skipping the table
                // and timings below - "/cite <n>" is a focused view, not the index plus an extra.
                var parts = context.RawCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var n) && n >= 1 && n <= result.Chunks.Count)
                {
                    RenderSource(console, result.Chunks[n - 1], n);
                    return;
                }

                RenderTable(console, result.Chunks);

                if (parts.Length >= 2)
                {
                    console.MarkupLine($"[grey]That's not a source number - pick 1 to {result.Chunks.Count}, e.g. /cite 1.[/]");
                }
            }

            var timings = result?.Timings;
            if (timings != null)
            {
                console.MarkupLine(
                    $"[grey]{timings.ChunksScanned} chunks scanned in {timings.SearchMs:F3} ms " +
                    $"({timings.ChunksReturned} above cutoff) — embedding took {timings.EmbeddingMs:F1} ms.[/]");
            }
        }

        private static void RenderTable(IAnsiConsole console, List<CitationChunkResult> chunks)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold cyan]SOURCES[/]");
            table.AddColumn("#").AddColumn("Source").AddColumn("Score").AddColumn("Preview");

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];

                // Every rendered string - source label and preview alike - is free text out
                // of the knowledge base, so both go through the same redaction boundary
                // MenuCommandService uses (Task 21D/BL-8): a future free-text field is
                // unprotected by default unless it's routed through RedactPhone explicitly.
                var label = SanitizerExtension.RedactPhone(chunk.Label).EscapeMarkup();
                var preview = SanitizerExtension.RedactPhone(BuildPreview(chunk)).EscapeMarkup();
                table.AddRow((i + 1).ToString(), label, chunk.Score.ToString("F3"), preview);
            }

            console.Write(table);
        }

        // Task 27C: /cite <n> renders one source in full - the whole chunk, not the trimmed
        // Preview column - wrapped to the terminal width (Spectre's Markup wraps to the console's
        // width automatically, same as every other MarkupLine call in this command). Same
        // redaction boundary as the table: this is more free text out of the knowledge base, not
        // less surface for an unredacted phone number to slip through.
        private static void RenderSource(IAnsiConsole console, CitationChunkResult chunk, int n)
        {
            var label = SanitizerExtension.RedactPhone(chunk.Label).EscapeMarkup();
            console.MarkupLine($"[bold cyan]SOURCE {n}[/] — [grey]{label}[/] (score {chunk.Score:F3})");
            console.MarkupLine(SanitizerExtension.RedactPhone(FullContent(chunk)).EscapeMarkup());
        }

        // Task 27A: the AI service now sends the whole chunk (FullText); Preview is kept as the
        // capped fallback for a gateway one version ahead of an AI service that hasn't shipped
        // FullText yet - deserializing the missing field just leaves "".
        private static string FullContent(CitationChunkResult chunk) =>
            string.IsNullOrEmpty(chunk.FullText) ? chunk.Preview : chunk.FullText;

        // Task 27B: chunk text is "topic: X. detail: Y" - splitting on the first ": " (what the
        // old Preview did) put "topic: ..." in the column and ate ~40% of the visible budget.
        // Preferring the text after "detail: " buys that back; falling back to the whole content
        // keeps this sensible for a chunk that has no "detail: " segment at all.
        private const string DetailMarker = "detail: ";

        private static string BuildPreview(CitationChunkResult chunk)
        {
            var content = FullContent(chunk);
            var idx = content.IndexOf(DetailMarker, StringComparison.Ordinal);
            var preferred = idx >= 0 ? content[(idx + DetailMarker.Length)..] : content;
            return preferred.Length > PreviewMaxLength ? preferred[..PreviewMaxLength] + "…" : preferred;
        }
    }
}
