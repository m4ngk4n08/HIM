using Microsoft.ML.Tokenizers;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 13 Part A regression pin: no chunk FlattenJson produces may exceed the 512-token cap
/// EmbeddingService.GetNormalizeLocalEmbeddingAsync silently truncates at. Measured with the
/// real BERT tokenizer (word counts are only indicative), not word counts, per the plan.
/// </summary>
[Collection(RealKnowledgeBaseCollection.Name)]
public class ChunkTokenLimitTests
{
    private const int MaxSequenceLength = 512; // EmbeddingService.GetNormalizeLocalEmbeddingAsync

    private readonly RealKnowledgeBaseFixture _fixture;

    public ChunkTokenLimitTests(RealKnowledgeBaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void KnowledgeBase_ProducesMoreThanAFewChunks()
    {
        // Sanity check on the fixture itself: if this is small, the zero-vector "fetch everything"
        // trick in RealKnowledgeBaseFixture silently returned nothing and the tests below are void.
        Assert.True(_fixture.AllChunks.Count > 10, $"Expected >10 chunks, got {_fixture.AllChunks.Count}.");
    }

    [Fact]
    public void NoChunk_ExceedsTheEmbeddingTruncationLimit()
    {
        var tokenizer = BertTokenizer.Create(
            Path.Combine(AppContext.BaseDirectory, "Models", "AllMiniLML6V2", "vocab.txt"));

        var offenders = _fixture.AllChunks
            .Select(c => (c.Text, TokenCount: tokenizer.EncodeToIds(c.Text).Count))
            .Where(c => c.TokenCount > MaxSequenceLength)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Chunk(s) exceed the embedding model's token cap and will be silently truncated:\n" +
            string.Join("\n", offenders.Select(o => $"  [{o.TokenCount} tokens] {o.Text[..Math.Min(80, o.Text.Length)]}...")));
    }
}
