using BenchmarkDotNet.Attributes;
using HIM.AiService.Services.AI;

namespace HIM.Benchmarks.Benchmarks;

// all-minilm-l6-v2 produces 384-dimensional embeddings, so 384 is the real workload size, not an
// arbitrary round number. Vector<float>.Count is 8 on AVX2 and 16 on AVX-512, and 384 divides
// evenly by both, so the scalar tail loop at VectorSearchService.cs:27-30 never executes for this
// input on this hardware - that is expected, not a bug in the benchmark.
[MemoryDiagnoser]
public class DotProductBenchmarks
{
    private const int Dimensions = 384;

    private readonly VectorSearchService _vectorSearchService = new();
    private float[] _a = null!;
    private float[] _b = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _a = CreateNormalizedVector(seed: 1);
        _b = CreateNormalizedVector(seed: 2);
    }

    [Benchmark(Baseline = true)]
    public float Scalar()
    {
        float result = 0;
        for (int i = 0; i < Dimensions; i++)
            result += _a[i] * _b[i];
        return result;
    }

    [Benchmark]
    public float Simd() => _vectorSearchService.CalculateDotProduct(_a, _b);

    // Deterministic (fixed seed) so re-runs are comparable, and L2-normalized so the input
    // matches what real embeddings look like going into vector search.
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
