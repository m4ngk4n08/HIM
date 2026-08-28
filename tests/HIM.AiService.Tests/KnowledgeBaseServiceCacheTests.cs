using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HIM.AiService.Tests;

public class KnowledgeBaseServiceCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourcePath;
    private readonly string _cachePath;

    public KnowledgeBaseServiceCacheTests()
    {
        _tempDir = Directory.CreateTempSubdirectory("kb-cache-tests").FullName;
        _sourcePath = Path.Combine(_tempDir, "knowledge-base.json");
        _cachePath = Path.Combine(_tempDir, "knowledge-base.embeddings.bin");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
    }

    private KnowledgeBaseService CreateService(Mock<IEmbeddingService> embeddingMock)
    {
        var settings = Options.Create(new AiSettings
        {
            KnowledgeBase = new KnowledgeBaseSettings
            {
                FilePath = _sourcePath,
                CacheFile = _cachePath
            }
        });

        var vectorSearchMock = new Mock<IVectorSearchService>();

        return new KnowledgeBaseService(
            embeddingMock.Object,
            vectorSearchMock.Object,
            settings,
            NullLogger<KnowledgeBaseService>.Instance);
    }

    private static Mock<IEmbeddingService> NewEmbeddingMock()
    {
        var mock = new Mock<IEmbeddingService>();
        mock.Setup(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync((string text) => new float[] { text.Length, 1f, 2f });
        return mock;
    }

    private void WriteSource(string json) => File.WriteAllText(_sourcePath, json);

    [Fact]
    public async Task InitializeAsync_RebuildsAndCaches_WhenNoCacheExists()
    {
        WriteSource("""{"name": "Angelo"}""");
        var embeddingMock = NewEmbeddingMock();
        var service = CreateService(embeddingMock);

        await service.InitializeAsync();

        Assert.True(File.Exists(_cachePath));
        embeddingMock.Verify(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitializeAsync_CacheHit_DoesNotRecomputeEmbeddings_WhenSourceUnchanged()
    {
        WriteSource("""{"name": "Angelo"}""");
        var firstEmbeddingMock = NewEmbeddingMock();
        var firstService = CreateService(firstEmbeddingMock);
        await firstService.InitializeAsync();

        var secondEmbeddingMock = NewEmbeddingMock();
        var secondService = CreateService(secondEmbeddingMock);
        await secondService.InitializeAsync();

        secondEmbeddingMock.Verify(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_Rebuilds_WhenSourceContentChanges()
    {
        WriteSource("""{"name": "Angelo"}""");
        var firstMock = NewEmbeddingMock();
        await CreateService(firstMock).InitializeAsync();

        // Simulate the real incident: source edited after the cache was built.
        WriteSource("""{"name": "Someone Else"}""");

        var secondMock = NewEmbeddingMock();
        await CreateService(secondMock).InitializeAsync();

        secondMock.Verify(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitializeAsync_Rebuilds_WhenCacheSchemaVersionDiffers()
    {
        WriteSource("""{"name": "Angelo"}""");
        await CreateService(NewEmbeddingMock()).InitializeAsync();

        // Corrupt just the schema-version field of an otherwise well-formed header.
        using (var stream = new FileStream(_cachePath, FileMode.Open, FileAccess.ReadWrite))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Seek(4, SeekOrigin.Begin); // magic (4 bytes) precedes version
            writer.Write(int.MaxValue);
        }

        var secondMock = NewEmbeddingMock();
        await CreateService(secondMock).InitializeAsync();

        secondMock.Verify(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitializeAsync_Rebuilds_WithoutThrowing_WhenCacheFileIsTruncatedGarbage()
    {
        WriteSource("""{"name": "Angelo"}""");
        await File.WriteAllBytesAsync(_cachePath, new byte[] { 1, 2, 3 });

        var mock = NewEmbeddingMock();
        var service = CreateService(mock);

        var exception = await Record.ExceptionAsync(() => service.InitializeAsync());

        Assert.Null(exception);
        mock.Verify(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitializeAsync_Rebuilds_WhenCacheFileHasNoHeader_LegacyFormat()
    {
        WriteSource("""{"name": "Angelo"}""");

        // Legacy headerless format: just an int32 chunk count, no magic/version/hash.
        using (var stream = File.Create(_cachePath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0);
        }

        var mock = NewEmbeddingMock();
        await CreateService(mock).InitializeAsync();

        mock.Verify(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }
}
