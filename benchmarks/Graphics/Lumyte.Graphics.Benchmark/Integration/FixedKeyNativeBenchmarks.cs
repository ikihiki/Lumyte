using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>Same complete CPU recording boundary, with explicit attachment-count controls.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class FixedKeyNativeBenchmarks
{
    private NativeModelWorkload _workload = null!;

    /// <summary>Gets or sets draw/state count.</summary>
    [Params(1, 32)]
    public int Count { get; set; }

    /// <summary>Gets or sets color attachment count.</summary>
    [Params(1, 8)]
    public int Targets { get; set; }

    /// <summary>Warms native variants and verifies pixels outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _workload = new();
        _workload.Verify();
        _workload.Verify(Targets);
        if (_workload.RecordInstances(Count, targetCount: Targets) != (ulong)Count)
        {
            throw new InvalidOperationException("Recording did not execute.");
        }
    }

    /// <summary>Records and discards one complete CPU frame.</summary>
    /// <returns>Draw count.</returns>
    [Benchmark]
    public ulong RecordIndexed() => _workload.RecordInstances(Count, targetCount: Targets);

    /// <summary>Releases retained native resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
