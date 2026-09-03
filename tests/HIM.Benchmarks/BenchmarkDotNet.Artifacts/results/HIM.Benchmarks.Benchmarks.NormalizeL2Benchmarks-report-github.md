```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9168)
11th Gen Intel Core i5-11400 2.60GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.400
  [Host]   : .NET 10.0.11 (10.0.1126.37416), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 10.0.11 (10.0.1126.37416), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  InvocationCount=1  IterationCount=3  
LaunchCount=1  UnrollFactor=1  WarmupCount=3  

```
| Method | Mean     | Error      | StdDev    | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------- |---------:|-----------:|----------:|---------:|------:|--------:|----------:|------------:|
| Scalar | 6.367 μs |   2.787 μs | 0.1528 μs | 6.400 μs |  1.00 |    0.03 |         - |          NA |
| Simd   | 9.200 μs | 164.325 μs | 9.0072 μs | 4.100 μs |  1.45 |    1.23 |         - |          NA |
