using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 14D (SEC-02), the ingestion half: PII must never enter the vector store, applied where
/// chunks are built - before embedding. Uses a mocked IEmbeddingService (a real embedding is
/// unnecessary here; RealKnowledgeBaseFixture already covers the true ONNX path for retrieval
/// quality) so this only exercises KnowledgeBaseService's own chunking + redaction, not the
/// embedding model.
/// </summary>
public class KnowledgeBaseIngestionRedactionTests : IDisposable
{
    // A fictional NANP number (555-01xx is reserved for fiction) - a stand-in canary, never the
    // real retired number.
    private const string Canary = "555-010-2020";
    private const string RedactedMarker = "[REDACTED_PHONE]";

    private readonly string _tempDir;
    private readonly string _sourcePath;
    private readonly string _cachePath;

    public KnowledgeBaseIngestionRedactionTests()
    {
        _tempDir = Directory.CreateTempSubdirectory("kb-redaction-tests").FullName;
        _sourcePath = Path.Combine(_tempDir, "knowledge-base.json");
        _cachePath = Path.Combine(_tempDir, "knowledge-base.embeddings.bin");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
    }

    private KnowledgeBaseService CreateService()
    {
        var settings = Options.Create(new AiSettings
        {
            KnowledgeBase = new KnowledgeBaseSettings
            {
                FilePath = _sourcePath,
                CacheFile = _cachePath
            }
        });

        var embeddingMock = new Mock<IEmbeddingService>();
        embeddingMock
            .Setup(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync((string text) => new float[] { text.Length, 1f, 2f });

        // A real VectorSearchService, not a mock: it's a plain SIMD dot product with no ONNX
        // dependency, and Moq can't proxy IVectorSearchService's ReadOnlySpan<float> parameters.
        return new KnowledgeBaseService(
            embeddingMock.Object,
            new VectorSearchService(),
            settings,
            NullLogger<KnowledgeBaseService>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_NeverStoresACanaryPhoneNumber_EvenIfOneIsInTheSourceFile()
    {
        File.WriteAllText(_sourcePath, $$"""
            {
                "personal_info": {
                    "summary": "Reach out at {{Canary}} for anything urgent.",
                    "contact": { "email": "angelodavales0528@gmail.com" }
                }
            }
            """);

        var service = CreateService();
        await service.InitializeAsync();

        // A zero query vector has equal (zero) similarity to every stored chunk, so with topK
        // above the real chunk count every chunk comes back - the only way to enumerate them all
        // through the public SearchAsync API.
        var allChunks = await service.SearchAsync(new float[3], topK: 1000);

        Assert.NotEmpty(allChunks);
        Assert.All(allChunks, chunk => Assert.DoesNotContain(Canary, chunk.Text));
        Assert.Contains(allChunks, chunk => chunk.Text.Contains(RedactedMarker));

        // The legitimate public contact channel must still make it into the vector store.
        Assert.Contains(allChunks, chunk => chunk.Text.Contains("angelodavales0528@gmail.com"));
    }
}
