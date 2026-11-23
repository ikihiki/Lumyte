using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>CPU wall completion latency (depth 1) or bounded headless throughput (depth 3); not GPU timestamps.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class MapFramesBenchmarks
{
    private MapFrameWorkload _workload = null!;

    /// <summary>Gets or sets a value indicating whether executes the deterministic map workload contract.</summary>
    [Params(false, true)]
    public bool RetainedMapView { get; set; }

    /// <summary>Gets or sets a value indicating whether the M3 camera path advances.</summary>
    [Params(false, true)]
    public bool FixedPanContinuousZoom { get; set; }

    /// <summary>Gets or sets the bounded independent slot count.</summary>
    [Params(1, 3)]
    public int FramesInFlight { get; set; }

    /// <summary>Executes the named map phase with real fixture content.</summary>
    /// <returns>Untimed warmup and oracle completion.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _workload = new(RetainedMapView, FixedPanContinuousZoom, FramesInFlight);
        Console.WriteLine(_workload.Identity);
        await _workload.RunAsync();
        await _workload.VerifyLastFrameAsync();
    }

    /// <summary>Executes the deterministic map workload contract.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Benchmark(OperationsPerInvoke = MapFrameWorkload.FramesPerOperation)]
    public Task CompletedFramesAsync() => _workload.RunAsync();

    /// <summary>Executes the deterministic map workload contract.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await _workload.VerifyLastFrameAsync();
        }
        finally
        {
            await _workload.DisposeAsync();
        }
    }
}
