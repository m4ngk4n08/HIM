using BenchmarkDotNet.Attributes;

namespace HIM.Benchmarks.Benchmarks;

// Proves the harness runs end to end before any real measurement depends on it.
[MemoryDiagnoser]
public class HarnessCheckBenchmarks
{
    [Benchmark]
    public int NoOp() => 1 + 1;
}
