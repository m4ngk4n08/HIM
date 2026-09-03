using BenchmarkDotNet.Attributes;
using HIM.AiService.Services.AI;

namespace HIM.Benchmarks.Benchmarks;

/// <summary>
/// Measures "how long does one visitor question spend in vector search" - the search loop at
/// KnowledgeBaseService.cs:320-326: a dot product per chunk, pushed through a PriorityQueue
/// capped at topK. Production currently holds 57 chunks (verified live 2026-09-03), so that is
/// the chunk count benchmarked here, each at the real 384-dimensional embedding size.
///
/// This deliberately does not go through KnowledgeBaseService/EmbeddingService itself - that would
/// pull in ONNX inference and file I/O, which is explicitly out of scope ("do not include
/// embedding generation"). Instead it reproduces the exact loop shape against synthetic
/// deterministic vectors and the real VectorSearchService, so the number reflects only the search
/// cost the brief asked for.
/// </summary>
[MemoryDiagnoser]
public class RetrievalBenchmarks
{
    private const int Dimensions = 384;
    private const int ChunkCount = 57;
    private const int TopK = 3;

    private readonly VectorSearchService _vectorSearchService = new();
    private float[][] _chunkVectors = null!;
    private float[] _queryVector = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _chunkVectors = new float[ChunkCount][];
        for (int i = 0; i < ChunkCount; i++)
            _chunkVectors[i] = CreateNormalizedVector(seed: 100 + i);

        _queryVector = CreateNormalizedVector(seed: 1);
    }

    [Benchmark]
    public int SearchTopK()
    {
        // TElement is the chunk reference itself, matching KnowledgeBaseService.SearchAsync's
        // PriorityQueue<(KnowledgeChunks Chunk, float Score), float> shape rather than a bare int.
        var pq = new PriorityQueue<float[], float>();

        foreach (var chunkVector in _chunkVectors)
        {
            float similarity = _vectorSearchService.CalculateDotProduct(_queryVector, chunkVector);
            pq.Enqueue(chunkVector, similarity); // Min-heap: Dequeue drops the smallest score

            if (pq.Count > TopK) pq.Dequeue();
        }

        return pq.Count;
    }

    private static float[] CreateNormalizedVector(int seed)
    {
        var random = new Random(seed);
        var vector = new float[Dimensions];
        float sumSquares = 0;

        for (int i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
            sumSquares += vector[i] * vector[i];
        }

        float invNorm = 1.0f / MathF.Sqrt(sumSquares);
        for (int i = 0; i < Dimensions; i++)
            vector[i] *= invNorm;

        return vector;
    }
}
