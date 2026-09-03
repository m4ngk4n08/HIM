using BenchmarkDotNet.Attributes;
using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using Microsoft.Extensions.Options;

namespace HIM.Benchmarks.Benchmarks;

// Same 384-dimensional shape as DotProductBenchmarks - the real all-minilm-l6-v2 output size.
[MemoryDiagnoser]
public class NormalizeL2Benchmarks
{
    private const int Dimensions = 384;

    private EmbeddingService _embeddingService = null!;
    private float[] _source = null!;

    // NormalizeL2 writes in place, so each iteration needs a fresh copy of the source vector -
    // otherwise every iteration after the first is normalizing an already-normalized vector
    // (a no-op scale by ~1.0), and the measured number would be fiction.
    private float[] _scalarWorking = null!;
    private float[] _simdWorking = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _source = CreateVector(seed: 1);

        _embeddingService = new EmbeddingService(
            new HttpClient(),
            Options.Create(new AiSettings
            {
                Onnx = new Onnx
                {
                    Tokenizer = "Models/AllMiniLML6V2/vocab.txt",
                    Model = "Models/AllMiniLML6V2/model.onnx"
                }
            }),
            new VectorSearchService());
    }

    [IterationSetup(Target = nameof(Scalar))]
    public void IterationSetupScalar() => _scalarWorking = (float[])_source.Clone();

    [IterationSetup(Target = nameof(Simd))]
    public void IterationSetupSimd() => _simdWorking = (float[])_source.Clone();

    [Benchmark(Baseline = true)]
    public void Scalar()
    {
        var vector = _scalarWorking;
        float sum = 0;
        for (int i = 0; i < vector.Length; i++)
            sum += vector[i] * vector[i];

        float norm = MathF.Sqrt(sum);
        if (norm < 1e-10f) return;

        float invNorm = 1.0f / norm;
        for (int i = 0; i < vector.Length; i++)
            vector[i] *= invNorm;
    }

    [Benchmark]
    public void Simd() => _embeddingService.NormalizeL2(_simdWorking);

    private static float[] CreateVector(int seed)
    {
        var random = new Random(seed);
        var vector = new float[Dimensions];
        for (int i = 0; i < Dimensions; i++)
            vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        return vector;
    }
}
