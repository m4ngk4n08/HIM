using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using Xunit.Abstractions;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 15: "tell me about yourself" and its siblings are the single most likely opening
/// question a visitor asks, and before this task they scored below MinSimilarityScore -
/// production served the no-context fallback to the most common opener there is. Pinned here so
/// a future knowledge-base rewording or a raised cutoff can't silently regress this again.
///
/// Asserts against KnowledgeBaseSettings' actual default, not a hardcoded 0.3, so the test tracks
/// the production setting rather than a copy of it.
/// </summary>
[Collection(RealKnowledgeBaseCollection.Name)]
public class OpenerQuestionsRetrievalTests
{
    private readonly RealKnowledgeBaseFixture _fixture;
    private readonly ITestOutputHelper _output;
    private static readonly float ProductionCutoff = new KnowledgeBaseSettings().MinSimilarityScore;

    public OpenerQuestionsRetrievalTests(RealKnowledgeBaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [InlineData("tell me about yourself")]
    [InlineData("tell me more about you")]
    [InlineData("who are you?")]
    [InlineData("what do you do?")]
    public async Task OpenerQuestion_ClearsTheProductionCutoff(string question)
    {
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(question);

        var topScore = _fixture.AllChunks
            .Select(c => new VectorSearchService().CalculateDotProduct(queryVector, c.Vector))
            .DefaultIfEmpty(float.NegativeInfinity)
            .Max();
        _output.WriteLine($"\"{question}\" -> top cosine score {topScore:F3} (cutoff {ProductionCutoff:F3})");

        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 10, minScore: ProductionCutoff);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task OffTopicControl_StaysFilteredOut_EvenAfterTheOpenerEntries()
    {
        // Task 15's own ground rule: any fix that lets "weather in Tokyo" retrieve context is
        // the wrong fix, regardless of what it does for the openers.
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(
            "What is the weather in Tokyo today?");
        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 10, minScore: ProductionCutoff);

        Assert.Empty(results);
    }
}
