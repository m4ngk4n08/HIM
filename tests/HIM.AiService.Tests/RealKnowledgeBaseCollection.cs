namespace HIM.AiService.Tests;

/// <summary>
/// Shares one RealKnowledgeBaseFixture across every class that needs the real embedding pipeline.
///
/// This has to be a collection fixture rather than IClassFixture. Each class fixture gets its own
/// instance, xUnit runs test collections in parallel, and every instance deletes and rebuilds the
/// same test-knowledge-base.embeddings.bin path - so the three classes raced and the run failed
/// with "Access to the path ... is denied" on whichever fixture lost. Serially the same 41 tests
/// pass, which is what made this look green locally. One collection means one instance, one owner
/// of the cache file, and the ~90 MB ONNX model loaded once instead of three times.
/// </summary>
[CollectionDefinition(Name)]
public class RealKnowledgeBaseCollection : ICollectionFixture<RealKnowledgeBaseFixture>
{
    public const string Name = "RealKnowledgeBase";

    // Intentionally empty: xUnit only uses this class as the collection's marker.
}
