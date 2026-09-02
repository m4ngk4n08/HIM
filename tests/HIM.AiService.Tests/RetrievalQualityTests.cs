namespace HIM.AiService.Tests;

/// <summary>
/// Task 13's retrieval-quality gate: a fixed question set with expected chunk content, covering
/// both project entries since those are the ones the truncation defect was silently eating.
/// Runs the real embedding pipeline end to end (see RealKnowledgeBaseFixture) - a mocked
/// embedding service can't catch a truncation bug because it never truncates anything.
/// </summary>
[Collection(RealKnowledgeBaseCollection.Name)]
public class RetrievalQualityTests
{
    private readonly RealKnowledgeBaseFixture _fixture;

    public RetrievalQualityTests(RealKnowledgeBaseFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> FixedQuestionSet()
    {
        // Content drawn from the back half of each project entry (past the old 512-token
        // truncation point) - if these fail to retrieve, the chunking fix regressed.
        yield return new object[]
        {
            "What kernel-level firewall protection does the HIM project use?",
            "nftables"
        };
        yield return new object[]
        {
            "How is instrumentation added to an application in Project Loom?",
            "LoomProfile"
        };
        yield return new object[]
        {
            "What does Angelo do for a living?",
            "Full Stack"
        };
    }

    [Theory]
    [MemberData(nameof(FixedQuestionSet))]
    public async Task Question_RetrievesAChunkContainingTheExpectedFact(string question, string expectedFragment)
    {
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(question);
        // Matches RagService.TryGetContextAsync's production topK (10) - this is a retrieval
        // gate on "does the fact survive chunking and rank in the window that reaches the
        // prompt", not a precision-at-5 benchmark.
        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 10);

        Assert.Contains(results, c => c.Text.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HimProjectFirewallDetail_IsRetrievable_ProvingItSurvivedChunking()
    {
        // "nftables kernel-level firewall" sits well past the old 512-token cutoff in the HIM
        // project entry's raw text - this is the specific fact the old chunking silently dropped.
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(
            "Tell me about HIM's nftables firewall and Fail2Ban integration.");
        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 5);

        Assert.Contains(results, c => c.Text.Contains("nftables", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, c => c.Text.Contains("Fail2Ban", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoomProjectAotDetail_IsRetrievable_ProvingItSurvivedChunking()
    {
        // Loom's AOT binary profile sits deep in the entry, past the point the old single-chunk
        // approach truncated. Asserts on "Native AOT" rather than a specific megabyte figure so
        // the pin survives the binary getting bigger or smaller.
        var queryVector = await _fixture.EmbeddingService.GetNormalizeLocalEmbeddingAsync(
            "How small is the Project Loom binary and what runtime does it need installed?");
        var results = await _fixture.KbService.SearchAsync(queryVector, topK: 5);

        Assert.Contains(results, c => c.Text.Contains("Native AOT", StringComparison.OrdinalIgnoreCase));
    }
}
