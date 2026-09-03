using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 22B: /api/chat/cite surfaces the scores SearchAsync computes internally and discards.
/// Uses the real embedding pipeline and the real knowledge base (via RealKnowledgeBaseFixture) -
/// a mocked IKnowledgeBaseService would only prove the plumbing, not that real retrieval scores
/// come back sorted and above cutoff the way production data actually does.
/// </summary>
[Collection(RealKnowledgeBaseCollection.Name)]
public class RagServiceCitationsTests
{
    private readonly RealKnowledgeBaseFixture _fixture;
    private static readonly float ProductionCutoff = new KnowledgeBaseSettings().MinSimilarityScore;

    public RagServiceCitationsTests(RealKnowledgeBaseFixture fixture) => _fixture = fixture;

    private RagService CreateService(int maxQuestionLength = 500)
    {
        var settings = new AiSettings
        {
            ChatProvider = "Gemini",
            Gemini = new GeminiSettings { ModelId = "gemini-3.1-flash-lite", ApiKey = "test-key" },
            Security = new SecuritySettings { MaxQuestionLength = maxQuestionLength },
            KnowledgeBase = new KnowledgeBaseSettings { MinSimilarityScore = ProductionCutoff }
        };

        return new RagService(
            _fixture.EmbeddingService,
            _fixture.KbService,
            new DailyTokenBudgetTracker(Options.Create(settings)),
            Options.Create(settings),
            NullLogger<RagService>.Instance);
    }

    [Fact]
    public async Task OnTopicQuestion_ReturnsChunksWithDescendingScores_AllAtOrAboveCutoff()
    {
        var (result, error) = await CreateService().GetCitationsAsync("Tell me about his experience at Accenture");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Chunks);

        for (int i = 0; i < result.Chunks.Count; i++)
            Assert.True(result.Chunks[i].Score >= ProductionCutoff);

        for (int i = 1; i < result.Chunks.Count; i++)
            Assert.True(result.Chunks[i - 1].Score >= result.Chunks[i].Score);
    }

    [Fact]
    public async Task OffTopicQuestion_ReturnsEmptyChunkList_NotAnError()
    {
        var (result, error) = await CreateService().GetCitationsAsync("What is the weather in Tokyo today?");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Empty(result!.Chunks);
    }

    [Fact]
    public async Task Timings_ReportChunksScannedAndReturned()
    {
        var (result, _) = await CreateService().GetCitationsAsync("Tell me about his experience at Accenture");

        Assert.NotNull(result);
        Assert.Equal(_fixture.AllChunks.Count, result!.Timings.ChunksScanned);
        Assert.Equal(result.Chunks.Count, result.Timings.ChunksReturned);
    }

    [Fact]
    public async Task OverLengthQuestion_IsRejected_TheSameWayAskRejectsIt()
    {
        var service = CreateService(maxQuestionLength: 20);
        var overLongQuestion = new string('a', 21);

        var (result, error) = await service.GetCitationsAsync(overLongQuestion);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("too long", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AlreadyCancelledToken_ThrowsOperationCanceled_InsteadOfCompleting()
    {
        // Task 24A: the token was accepted but passed to nothing. Neither IEmbeddingService nor
        // IKnowledgeBaseService takes a CancellationToken, so this is checked at the two phase
        // boundaries inside GetCitationsAsync itself, not threaded into either dependency.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().GetCitationsAsync("Tell me about his experience at Accenture", cts.Token));
    }
}
