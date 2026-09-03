using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using Xunit.Abstractions;

namespace HIM.AiService.Tests;

/// <summary>
/// The knowledge base gained a "what_i_build" section and three Q&amp;A entries on 2026-09-03, so
/// the AI can answer what kind of engineer Angelo is - he builds developer tools - rather than
/// only listing two projects and leaving the reader to infer the pattern.
///
/// Pinned for the same reason as OpenerQuestionsRetrievalTests: content is only useful if it
/// actually retrieves, and a reworded entry or a raised cutoff could drop it below
/// MinSimilarityScore invisibly.
///
/// **These assert on retrieved content, not just on a non-empty result.** The first draft of this
/// file asserted NotEmpty like the opener tests do, and the mutation check exposed that as nearly
/// worthless here: deleting the whole new section still left four of five cases passing, because
/// unrelated chunks clear the cutoff for these questions anyway. NotEmpty is the right assertion
/// for the opener tests, where the failure being guarded against genuinely is "empty, so the
/// visitor gets the no-context fallback". Here the failure being guarded against is "the answer
/// retrieved is about something else", which only a content assertion can catch.
/// </summary>
[Collection(RealKnowledgeBaseCollection.Name)]
public class DeveloperToolsFocusRetrievalTests
{
    private readonly RealKnowledgeBaseFixture _fixture;
    private readonly ITestOutputHelper _output;
    private static readonly float ProductionCutoff = new KnowledgeBaseSettings().MinSimilarityScore;

    public DeveloperToolsFocusRetrievalTests(RealKnowledgeBaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [InlineData("what kind of engineer are you?", "developer tools")]
    [InlineData("what kind of software do you build?", "developer tools")]
    [InlineData("do you build developer tools?", "developer tools")]
    [InlineData("are you learning anything new?", "C")]
    [InlineData("what are you building next?", "migration")]
    public async Task DeveloperToolsQuestion_RetrievesTheRightSubject(string question, string expected)
    {
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(question);
        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 10, minScore: ProductionCutoff);

        _output.WriteLine($"\"{question}\" -> {results.Count} chunk(s) above {ProductionCutoff:F3}");

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Text.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PerformanceQuestion_RetrievesTheMeasuredFigures()
    {
        // The HIM project entry carries a measured_performance field rather than burying the
        // benchmark numbers inside key_technical_achievements - partly because performance is its
        // own topic and retrieves better as its own chunk, and partly because folding the numbers
        // into the achievements list pushed that chunk past the embedding model's 512-token cap.
        // ChunkTokenLimitTests caught that; this asserts the split actually retrieves.
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(
            "how fast is the vector search?");

        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 10, minScore: ProductionCutoff);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Text.Contains("BenchmarkDotNet", StringComparison.OrdinalIgnoreCase));
    }
}
