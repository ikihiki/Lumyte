using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Renderer;

/// <summary>CPU path preparation and recording without unused rectangle-count parameter expansion.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class PathRendererBenchmarks
{
    private DrawingCpuHarness _harness = null!;
    private DrawList2D _paths = null!;

    /// <summary>Builds immutable input and primes retained renderer resources outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _paths = DrawingWorkloads.ClippedGradientPaths();
        _harness = new();
        for (int prime = 0; prime < 4; prime++)
        {
            _harness.WarmPrepareRecordDispose(_paths);
        }
    }

    /// <summary>Creates a renderer, prepares and records the fixed sixteen-path clip/gradient workload.</summary>
    /// <returns>The bounded synthetic command checksum.</returns>
    [Benchmark]
    public ulong ClippedGradientPathsColdRenderer() => _harness.ColdPrepareRecordDispose(_paths);

    /// <summary>Prepares and records the same fixed path workload on a retained renderer.</summary>
    /// <returns>The bounded synthetic command checksum.</returns>
    [Benchmark]
    public ulong ClippedGradientPathsWarmRenderer() => _harness.WarmPrepareRecordDispose(_paths);

    /// <summary>Releases the retained renderer and target.</summary>
    [GlobalCleanup]
    public void Cleanup() => _harness.Dispose();
}
