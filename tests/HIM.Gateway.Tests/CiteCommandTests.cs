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

    private static async Task<string> RunCiteAsync(string? lastQuestion, (CitationResult? Result, string? Error) citationResponse)
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
        var context = new CommandContext(console, stream, "/cite", new PortfolioData(), "session", CancellationToken.None);
        await command.ExecuteAsync(context);

        return writer.ToString();
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

    private static async Task<string> ExecuteAsync(CiteCommand command)
    {
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
}
