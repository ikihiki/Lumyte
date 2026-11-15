using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Renderer;

/// <summary>CPU preparation and recording including bounded managed driver work and per-frame disposal; no GPU execution.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class RendererBenchmarks
{
    private DrawingCpuHarness _harness = null!;
    private DrawList2D _rectangles = null!;

    /// <summary>Gets or sets neighboring rectangle counts around the historical 1024-instance split, now represented by fixed-record ranges.</summary>
    [Params(1023, 1024, 1025)]
    public int RectangleCount { get; set; }

    /// <summary>Builds input separately and primes the warm renderer.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _rectangles = DrawingWorkloads.Rectangles(RectangleCount);
        _harness = new();
        for (int prime = 0; prime < 4; prime++)
        {
            _harness.WarmPrepareRecordDispose(_rectangles);
        }
    }

    /// <summary>Measures first preparation on a new renderer plus recording/finalization and renderer disposal, not process cold start.</summary>
    /// <returns>The synthetic-driver checksum.</returns>
    [Benchmark]
    public ulong RectanglesColdRenderer() => _harness.ColdPrepareRecordDispose(_rectangles);

    /// <summary>Measures preparation plus record/finalize and per-frame disposal on a retained renderer, not retained-scene preparation.</summary>
    /// <returns>The synthetic-driver checksum.</returns>
    [Benchmark]
    public ulong RectanglesWarmRenderer() => _harness.WarmPrepareRecordDispose(_rectangles);

    /// <summary>Releases all retained resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _harness.Dispose();
}
