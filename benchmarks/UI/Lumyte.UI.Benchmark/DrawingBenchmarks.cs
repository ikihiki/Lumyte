using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Benchmark;

/// <summary>Local CPU capture, explicit snapshots and managed renderer simulation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class DrawingBenchmarks
{
    private UiDrawingWorkload _workload = null!;

    /// <summary>Gets or sets actual rendered rectangle count.</summary>
    [Params(16, 128)]
    public int Count { get; set; }

    /// <summary>Creates and primes the owned scene.</summary>
    /// <returns>Managed simulation completion.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _workload = new(Count);
        for (int i = 0; i < 4; i++)
        {
            await _workload.RetainedSimulatedAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Changes one fill and reuses clean CPU local captures.</summary>
    [Benchmark]
    public void OneLocalCapture() => _workload.Update(false);

    /// <summary>Changes every fill and captures every local rectangle.</summary>
    [Benchmark]
    public void AllLocalCaptures() => _workload.Update(true);

    /// <summary>Includes a real mutation, update and immutable snapshot.</summary>
    /// <returns>The frozen revision.</returns>
    [Benchmark]
    public ulong Snapshot() => _workload.Snapshot();

    /// <summary>Standard public recording, preparation, managed submission and retirement.</summary>
    /// <returns>Managed simulation completion, not GPU execution.</returns>
    [Benchmark]
    public ValueTask StandardSimulatedAsync() => _workload.StandardSimulatedAsync();

    /// <summary>Retained preparation, recording and attached managed submission retirement.</summary>
    /// <returns>Managed simulation completion, not GPU execution.</returns>
    [Benchmark]
    public ValueTask RetainedSimulatedAsync() => _workload.RetainedSimulatedAsync();

    /// <summary>Disposes all owners.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
