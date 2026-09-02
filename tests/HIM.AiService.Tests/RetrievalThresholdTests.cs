using HIM.AiService.Models.AI;

namespace HIM.AiService.Tests;

/// <summary>
/// Pins KnowledgeBaseSettings.MinSimilarityScore against the real model and the real knowledge
/// base. The other retrieval tests call SearchAsync without minScore, so nothing else exercises
/// the production cutoff. Measured when the cutoff was introduced: an on-topic question about
/// Accenture tops out at ~0.34 - only ~0.04 clear of the 0.3 default - so this is the question
/// that fails first if the default is raised or the knowledge base is reworded.
/// </summary>
[Collection(RealKnowledgeBaseCollection.Name)]
public class RetrievalThresholdTests
{
    private readonly RealKnowledgeBaseFixture _fixture;
    private static readonly float ProductionCutoff = new KnowledgeBaseSettings().MinSimilarityScore;

    public RetrievalThresholdTests(RealKnowledgeBaseFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Tell me about his experience at Accenture")]
    [InlineData("What technologies does Angelo work with?")]
    [InlineData("Is Angelo open to relocation?")]
    [InlineData("What kernel-level firewall protection does the HIM project use?")]
    public async Task OnTopicQuestion_StillClearsTheProductionCutoff(string question)
    {
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(question);
        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 10, minScore: ProductionCutoff);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task OffTopicQuestion_IsFilteredOutEntirely()
    {
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(
            "What is the weather in Tokyo today?");
        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 10, minScore: ProductionCutoff);

        // This is what makes the fallback path reachable in production - if it ever returns
        // chunks, an off-topic question is being answered from irrelevant context instead.
        Assert.Empty(results);
    }
}
