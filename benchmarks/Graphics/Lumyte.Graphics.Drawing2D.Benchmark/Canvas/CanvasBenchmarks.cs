using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D.Benchmark.Renderer;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Canvas;

/// <summary>Pure canvas construction and snapshot publication, separate from device/renderer work.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class CanvasBenchmarks
{
    /// <summary>Gets or sets the boundary-sized canvas input.</summary>
    [Params(1023, 1024, 1025)]
    public int RectangleCount { get; set; }

    /// <summary>Records rectangles through the real Canvas2D API.</summary>
    /// <returns>The immutable drawing.</returns>
    [Benchmark]
    public DrawList2D Rectangles() => DrawingWorkloads.Rectangles(RectangleCount);
}
