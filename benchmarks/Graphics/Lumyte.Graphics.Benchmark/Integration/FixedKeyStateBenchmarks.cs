using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>State/key-only target-count coverage; zero targets has no draw or pixel claim.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class FixedKeyStateBenchmarks
{
    private NativeModelWorkload _workload = null!;

    /// <summary>Gets or sets state count.</summary>
    [Params(1, 32)]
    public int Count { get; set; }

    /// <summary>Gets or sets the matching pass/state color count.</summary>
    [Params(0, 1, 8)]
    public int Targets { get; set; }

    /// <summary>Checks state setting, without claiming a zero-target depth pixel oracle.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _workload = new();
        if (_workload.States(Count, Targets) != Count)
        {
            throw new InvalidOperationException("State setting did not execute.");
        }
    }

    /// <summary>Records state validation/snapshot/key plus matching command/pass lifetime.</summary>
    /// <returns>State count.</returns>
    [Benchmark]
    public int PrepareStates() => _workload.States(Count, Targets);

    /// <summary>Releases retained resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
