using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HIM.AiService.Tests;

/// <summary>
/// Loads the real ONNX embedding model, real BERT tokenizer, and the real knowledge-base.json
/// once per test class (loading the ~90 MB ONNX model is the expensive part). Task 13's Part A
/// fix can only be verified end-to-end this way - a mocked IEmbeddingService bypasses the exact
/// code path (EmbeddingService.GetNormalizeLocalEmbeddingAsync's 512-token truncation) that was
/// silently eating the back half of the two flagship project entries.
/// </summary>
public class RealKnowledgeBaseFixture : IAsyncLifetime
{
    public IEmbeddingService EmbeddingService { get; private set; } = null!;
    public IKnowledgeBaseService KbService { get; private set; } = null!;
    public List<KnowledgeChunks> AllChunks { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var baseDir = AppContext.BaseDirectory;
        var cacheFile = Path.Combine(baseDir, "test-knowledge-base.embeddings.bin");

        // Force a fresh build every run - a stale cache from a previous run is exactly the
        // "cache trap" Task 13 calls out: the source bytes don't change when only the chunking
        // logic changes, so a leftover cache would make these tests validate nothing.
        if (File.Exists(cacheFile)) File.Delete(cacheFile);

        var settings = new AiSettings
        {
            Onnx = new Onnx
            {
                Tokenizer = Path.Combine(baseDir, "Models", "AllMiniLML6V2", "vocab.txt"),
                Model = Path.Combine(baseDir, "Models", "AllMiniLML6V2", "model.onnx")
            },
            KnowledgeBase = new KnowledgeBaseSettings
            {
                FilePath = Path.Combine(baseDir, "knowledge-base.json"),
                CacheFile = cacheFile
            }
        };

        var options = Options.Create(settings);
        var vectorSearch = new VectorSearchService();

        EmbeddingService = new EmbeddingService(new HttpClient(), options, vectorSearch);
        KbService = new KnowledgeBaseService(EmbeddingService, vectorSearch, options, NullLogger<KnowledgeBaseService>.Instance);

        await KbService.InitializeAsync();

        // A zero query vector has zero dot-product similarity to every (L2-normalized) chunk
        // vector, so with topK comfortably above the real chunk count every chunk comes back -
        // this is the only way to enumerate them all through the public SearchAsync API.
        AllChunks = await KbService.SearchAsync(new float[384], topK: 1000);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
