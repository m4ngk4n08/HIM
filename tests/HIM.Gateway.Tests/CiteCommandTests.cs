using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Commands;
using HIM.Gateway.Services.SSH.Interfaces;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 22C: /cite explains the last question asked in this session, using scores and timings
/// from the AI service's /api/chat/cite endpoint (Task 22B). Drives the real CiteCommand with a
/// fake IAiClientService standing in for that HTTP call.
/// </summary>
public class CiteCommandTests
{
    private const string Canary = "555-010-2020";

    private class FakeAiClientService((CitationResult? Result, string? Error) response) : IAiClientService
    {
        public string? LastQuestionAsked { get; private set; }

        public IAsyncEnumerable<string> GetAiResponseAsync(string question, CancellationToken ct, string? correlationId = null)
            => AsyncEnumerable.Empty<string>();

        public Task<(CitationResult? Result, string? Error)> GetCitationsAsync(string question, CancellationToken ct, string? correlationId = null)
        {
            LastQuestionAsked = question;
            return Task.FromResult(response);
        }
    }

    // Task 23A: a queue of responses, one per expected call - lets a test assert exactly how many
    // times the AI client was actually reached, same counting-fake shape as CommandRoutingTests.
    private class CountingAiClientService : IAiClientService
    {
        private readonly Queue<(CitationResult? Result, string? Error)> _responses;
        public int CallCount { get; private set; }

        public CountingAiClientService(params (CitationResult? Result, string? Error)[] responses)
        {
            _responses = new Queue<(CitationResult? Result, string? Error)>(responses);
        }

        public IAsyncEnumerable<string> GetAiResponseAsync(string question, CancellationToken ct, string? correlationId = null)
            => AsyncEnumerable.Empty<string>();

        public Task<(CitationResult? Result, string? Error)> GetCitationsAsync(string question, CancellationToken ct, string? correlationId = null)
        {
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static async Task<string> RunCiteAsync(string? lastQuestion, (CitationResult? Result, string? Error) citationResponse, string rawCommand = "/cite")
    {
        var sessionState = new UserSessionState { LastQuestion = lastQuestion };
        var command = new CiteCommand(new FakeAiClientService(citationResponse), sessionState);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });

        using var stream = new MemoryStream();
        var context = new CommandContext(console, stream, rawCommand, new PortfolioData(), "session", CancellationToken.None);
        await command.ExecuteAsync(context);

        return writer.ToString();
    }

    private static CitationResult MakeMultiSourceResult(params string[] fullTexts)
    {
        var chunks = fullTexts.Select((text, i) => new CitationChunkResult
        {
            Label = $"source{i + 1}",
            Score = 0.9f - (i * 0.1f),
            FullText = text
        }).ToList();

        return new CitationResult
        {
            Question = "q",
            Chunks = chunks,
            Timings = new CitationTimingsResult { EmbeddingMs = 1, SearchMs = 1, ChunksScanned = chunks.Count, ChunksReturned = chunks.Count }
        };
    }

    [Fact]
    public async Task NoPreviousQuestion_PrintsAHint_NotAnError()
    {
        var output = await RunCiteAsync(lastQuestion: null, citationResponse: (null, null));

        Assert.Contains("Ask something first", output);
    }

    [Fact]
    public async Task ChunkPreviewContainingACanary_RendersRedacted()
    {
        var result = new CitationResult
        {
            Question = "What's Angelo's number?",
            Chunks =
            [
                new CitationChunkResult
                {
                    Label = "stress_test_qna",
                    Score = 0.5f,
                    Preview = $"Reach out at {Canary} for more."
                }
            ],
            Timings = new CitationTimingsResult { EmbeddingMs = 3, SearchMs = 0.006, ChunksScanned = 65, ChunksReturned = 1 }
        };

        var output = await RunCiteAsync("What's Angelo's number?", (result, null));

        Assert.DoesNotContain(Canary, output);
        Assert.Contains("[REDACTED_PHONE]", output);
    }

    [Fact]
    public async Task ChunkLabelContainingACanary_AlsoRendersRedacted()
    {
        var result = new CitationResult
        {
            Question = "q",
            Chunks =
            [
                new CitationChunkResult { Label = $"weird [{Canary}]", Score = 0.4f, Preview = "harmless text" }
            ],
            Timings = new CitationTimingsResult { EmbeddingMs = 1, SearchMs = 1, ChunksScanned = 65, ChunksReturned = 1 }
        };

        var output = await RunCiteAsync("q", (result, null));

        Assert.DoesNotContain(Canary, output);
        Assert.Contains("[REDACTED_PHONE]", output);
    }

    [Fact]
    public async Task ErrorTextContainingACanary_AlsoRendersRedacted()
    {
        var output = await RunCiteAsync("q", (null, $"Something failed near {Canary}."));

        Assert.DoesNotContain(Canary, output);
        Assert.Contains("[REDACTED_PHONE]", output);
    }

    [Fact]
    public async Task PreviewColumn_PrefersTextAfterDetailMarker_OverTopicPrefix()
    {
        var result = new CitationResult
        {
            Question = "q",
            Chunks =
            [
                new CitationChunkResult
                {
                    Label = "what_i_build",
                    Score = 0.5f,
                    FullText = "topic: X. detail: the real content that matters."
                }
            ],
            Timings = new CitationTimingsResult { EmbeddingMs = 1, SearchMs = 1, ChunksScanned = 1, ChunksReturned = 1 }
        };

        var output = await RunCiteAsync("q", (result, null));

        Assert.Contains("the real content that matters", output);
        Assert.DoesNotContain("topic: X", output);
    }

    [Fact]
    public async Task PreviewColumn_WithNoDetailMarker_FallsBackToWholeContent()
    {
        var result = new CitationResult
        {
            Question = "q",
            Chunks =
            [
                new CitationChunkResult
                {
                    Label = "career_break_note",
                    Score = 0.5f,
                    FullText = "no marker here"
                }
            ],
            Timings = new CitationTimingsResult { EmbeddingMs = 1, SearchMs = 1, ChunksScanned = 1, ChunksReturned = 1 }
        };

        var output = await RunCiteAsync("q", (result, null));

        Assert.Contains("no marker here", output);
    }

    [Fact]
    public async Task PreviewColumn_FallsBackToPreview_WhenFullTextIsMissing()
    {
        // Task 27A: a gateway ahead of an AI service that hasn't shipped FullText yet - the
        // field deserializes as "" - must not render an empty column.
        var result = new CitationResult
        {
            Question = "q",
            Chunks = [new CitationChunkResult { Label = "l", Score = 0.5f, Preview = "old-style preview text", FullText = "" }],
            Timings = new CitationTimingsResult { EmbeddingMs = 1, SearchMs = 1, ChunksScanned = 1, ChunksReturned = 1 }
        };

        var output = await RunCiteAsync("q", (result, null));

        Assert.Contains("old-style preview text", output);
    }

    [Fact]
    public async Task Table_NumbersRowsFromOne_InRenderedOrder()
    {
        var result = new CitationResult
        {
            Question = "q",
            Chunks =
            [
                new CitationChunkResult { Label = "first", Score = 0.9f, FullText = "first content" },
                new CitationChunkResult { Label = "second", Score = 0.5f, FullText = "second content" }
            ],
            Timings = new CitationTimingsResult { EmbeddingMs = 1, SearchMs = 1, ChunksScanned = 2, ChunksReturned = 2 }
        };

        var output = await RunCiteAsync("q", (result, null));

        Assert.Contains("#", output);
        var lines = output.Split('\n');
        var firstLine = Assert.Single(lines, l => l.Contains("first"));
        var secondLine = Assert.Single(lines, l => l.Contains("second"));
        Assert.Contains("1", firstLine);
        Assert.Contains("2", secondLine);
    }

    [Fact]
    public async Task Timings_AreRendered_WithChunkCounts()
    {
        var result = new CitationResult
        {
            Question = "q",
            Chunks = [new CitationChunkResult { Label = "l", Score = 0.5f, Preview = "p" }],
            Timings = new CitationTimingsResult { EmbeddingMs = 3.1, SearchMs = 0.006, ChunksScanned = 65, ChunksReturned = 4 }
        };

        var output = await RunCiteAsync("q", (result, null));

        Assert.Contains("65 chunks scanned", output);
        Assert.Contains("4 above cutoff", output);
    }

    [Fact]
    public async Task NoChunksAboveCutoff_SaysSo_RatherThanRenderingAnEmptyTable()
    {
        var result = new CitationResult
        {
            Question = "q",
            Chunks = [],
            Timings = new CitationTimingsResult { EmbeddingMs = 3, SearchMs = 0.006, ChunksScanned = 65, ChunksReturned = 0 }
        };

        var output = await RunCiteAsync("q", (result, null));

        Assert.Contains("cleared the relevance cutoff", output);
    }

    [Fact]
    public async Task ExecuteAsync_AsksTheAiClientTheLastStoredQuestion()
    {
        var aiClient = new FakeAiClientService((null, null));
        var sessionState = new UserSessionState { LastQuestion = "What does Angelo build?" };
        var command = new CiteCommand(aiClient, sessionState);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        using var stream = new MemoryStream();
        var context = new CommandContext(console, stream, "/cite", new PortfolioData(), "session", CancellationToken.None);

        await command.ExecuteAsync(context);

        Assert.Equal("What does Angelo build?", aiClient.LastQuestionAsked);
    }

    private static CitationResult MakeResult(string question) => new()
    {
        Question = question,
        Chunks = [new CitationChunkResult { Label = "l", Score = 0.5f, Preview = question }],
        Timings = new CitationTimingsResult { EmbeddingMs = 1, SearchMs = 1, ChunksScanned = 1, ChunksReturned = 1 }
    };

    private static async Task<string> ExecuteAsync(CiteCommand command, string rawCommand = "/cite")
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        using var stream = new MemoryStream();
        var context = new CommandContext(console, stream, rawCommand, new PortfolioData(), "session", CancellationToken.None);
        await command.ExecuteAsync(context);
        return writer.ToString();
    }

    [Fact]
    public async Task RepeatCite_ForTheSameQuestion_DoesNotCallTheAiClientAgain()
    {
        var sessionState = new UserSessionState { LastQuestion = "q1" };
        var aiClient = new CountingAiClientService((MakeResult("q1"), null));
        var command = new CiteCommand(aiClient, sessionState);

        var first = await ExecuteAsync(command);
        var second = await ExecuteAsync(command);

        Assert.Equal(1, aiClient.CallCount);
        Assert.Contains("q1", first);
        Assert.Contains("q1", second);
    }

    [Fact]
    public async Task NewQuestion_AfterACachedCite_CallsTheClientAgain_AndRendersTheNewCitations()
    {
        var sessionState = new UserSessionState { LastQuestion = "q1" };
        var aiClient = new CountingAiClientService((MakeResult("q1"), null), (MakeResult("q2"), null));
        var command = new CiteCommand(aiClient, sessionState);

        await ExecuteAsync(command);
        sessionState.LastQuestion = "q2";
        var second = await ExecuteAsync(command);

        Assert.Equal(2, aiClient.CallCount);
        Assert.Contains("q2", second);
        Assert.DoesNotContain("q1", second);
    }

    [Fact]
    public async Task ErrorResult_IsNotCached_SoASucceedingRetryCallsTheClientAgain()
    {
        var sessionState = new UserSessionState { LastQuestion = "q1" };
        var aiClient = new CountingAiClientService((null, "transient failure"), (MakeResult("q1"), null));
        var command = new CiteCommand(aiClient, sessionState);

        var first = await ExecuteAsync(command);
        var second = await ExecuteAsync(command);

        Assert.Equal(2, aiClient.CallCount);
        Assert.Contains("Couldn't retrieve citations", first);
        Assert.Contains("q1", second);
    }

    // Task 27C tests: /cite <n>.

    [Fact]
    public async Task CiteWithIndex_RendersThatSourcesFullText_BeyondThePreviewCap()
    {
        var longText = "topic: X. detail: " + new string('z', 200);
        var result = MakeMultiSourceResult("first source content", longText);

        var output = await RunCiteAsync("q", (result, null), rawCommand: "/cite 2");

        // Console wrapping can split a 200-char run across lines, so count the character rather
        // than asserting on a contiguous substring - the point is that it isn't cut off at 150.
        Assert.True(output.Count(c => c == 'z') >= 200);
        Assert.Contains("SOURCE 2", output);
    }

    [Fact]
    public async Task CiteWithOutOfRangeIndex_RendersTheTablePlusAHint_NotAnError()
    {
        var result = MakeMultiSourceResult("a", "b");

        var output = await RunCiteAsync("q", (result, null), rawCommand: "/cite 99");

        Assert.Contains("SOURCES", output);
        Assert.Contains("not a source number", output);
    }

    [Fact]
    public async Task CiteWithNonNumericIndex_RendersTheTablePlusAHint_NotAnError()
    {
        var result = MakeMultiSourceResult("a", "b");

        var output = await RunCiteAsync("q", (result, null), rawCommand: "/cite banana");

        Assert.Contains("SOURCES", output);
        Assert.Contains("not a source number", output);
    }

    [Fact]
    public async Task CiteWithBadIndex_DoesNotClearTheCache()
    {
        var sessionState = new UserSessionState { LastQuestion = "q1" };
        var aiClient = new CountingAiClientService((MakeResult("q1"), null));
        var command = new CiteCommand(aiClient, sessionState);

        await ExecuteAsync(command, "/cite banana");

        Assert.NotNull(sessionState.CachedCitation);
        Assert.Equal("q1", sessionState.CachedCitation!.Question);

        // A follow-up plain /cite must still find the cache and not re-hit the AI service.
        await ExecuteAsync(command, "/cite");
        Assert.Equal(1, aiClient.CallCount);
    }

    [Fact]
    public async Task CiteWithIndex_UsesTheCache_MakesNoAiServiceCall()
    {
        var sessionState = new UserSessionState { LastQuestion = "q1" };
        var aiClient = new CountingAiClientService((MakeResult("q1"), null));
        var command = new CiteCommand(aiClient, sessionState);

        await ExecuteAsync(command, "/cite");
        await ExecuteAsync(command, "/cite 1");

        Assert.Equal(1, aiClient.CallCount);
    }

    [Fact]
    public async Task CiteWithIndex_FullTextContainingACanary_RendersRedacted()
    {
        var result = new CitationResult
        {
            Question = "What's Angelo's number?",
            Chunks = [new CitationChunkResult { Label = "l", Score = 0.5f, FullText = $"Reach out at {Canary} for more." }],
            Timings = new CitationTimingsResult { EmbeddingMs = 1, SearchMs = 1, ChunksScanned = 1, ChunksReturned = 1 }
        };

        var output = await RunCiteAsync("What's Angelo's number?", (result, null), rawCommand: "/cite 1");

        Assert.DoesNotContain(Canary, output);
        Assert.Contains("[REDACTED_PHONE]", output);
    }

    [Fact]
    public async Task CiteWithIndex_DoesNotIncrementAiBudgetOrTouchCooldown()
    {
        var sessionState = new UserSessionState { LastQuestion = "q1", AiQueryCount = 0, LastQuery = default };
        var aiClient = new CountingAiClientService((MakeResult("q1"), null));
        var command = new CiteCommand(aiClient, sessionState);

        await ExecuteAsync(command, "/cite 1");

        Assert.Equal(0, sessionState.AiQueryCount);
        Assert.Equal(default, sessionState.LastQuery);
    }
}
