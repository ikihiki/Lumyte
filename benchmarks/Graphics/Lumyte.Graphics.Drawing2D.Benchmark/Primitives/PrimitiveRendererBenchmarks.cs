using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Primitives;

/// <summary>Nine additional default renderer CPU entry points; sixty-four commands, no GPU or pixel claims.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class PrimitiveRendererBenchmarks
{
    private PrimitiveCpuFixture _fixture = null!;

    /// <summary>Gets or sets the actual public primitive command.</summary>
    [ParamsAllValues]
    public PrimitiveKind Primitive { get; set; }

    /// <summary>Builds immutable inputs and primes renderer resources outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fixture = new(Primitive);
        for (int i = 0; i < 4; i++)
        {
            _fixture.PrepareRecordDispose();
        }
    }

    /// <summary>Prepares and records the direct primitive list and releases per-frame owners.</summary>
    /// <returns>The actual managed upload/recording checksum.</returns>
    [Benchmark]
    public ulong PrepareRecordDispose() => _fixture.PrepareRecordDispose();

    /// <summary>Releases all managed fixture resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
