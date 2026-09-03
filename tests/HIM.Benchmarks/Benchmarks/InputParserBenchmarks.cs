using BenchmarkDotNet.Attributes;
using HIM.Gateway.Services.ServiceModel.Enums;
using HIM.Gateway.Services.SSH.Game;

namespace HIM.Benchmarks.Benchmarks;

/// <summary>
/// Measures GameInputService.GetNextInputAsync using a pre-filled MemoryStream, which completes
/// synchronously - the fast path through the compiler-generated async state machine. The span
/// parsing itself is genuinely allocation-free, but the method is `async Task&lt;GameInput&gt;`,
/// so if the state machine or the Task is ever boxed, MemoryDiagnoser will show it here.
///
/// What this does NOT measure: a real SSH network stream, where stream.ReadAsync does not
/// complete synchronously the way a MemoryStream read does. Production per-keystroke cost is
/// dominated by network I/O this benchmark deliberately excludes - it isolates the parser, not
/// the end-to-end read.
/// </summary>
[MemoryDiagnoser]
public class InputParserBenchmarks
{
    private readonly GameInputService _service = new();

    private readonly MemoryStream _arrowKeyStream = new(new byte[] { 0x1B, 0x5B, (byte)'A' }, writable: false);
    private readonly MemoryStream _singleKeyStream = new(new byte[] { (byte)'w' }, writable: false);

    [IterationSetup(Target = nameof(ArrowKeyEscapeSequence))]
    public void ResetArrowKeyStream() => _arrowKeyStream.Position = 0;

    [IterationSetup(Target = nameof(SingleByteKeypress))]
    public void ResetSingleKeyStream() => _singleKeyStream.Position = 0;

    [Benchmark]
    public async Task<GameInput> ArrowKeyEscapeSequence() =>
        await _service.GetNextInputAsync(_arrowKeyStream, CancellationToken.None);

    [Benchmark]
    public async Task<GameInput> SingleByteKeypress() =>
        await _service.GetNextInputAsync(_singleKeyStream, CancellationToken.None);
}
