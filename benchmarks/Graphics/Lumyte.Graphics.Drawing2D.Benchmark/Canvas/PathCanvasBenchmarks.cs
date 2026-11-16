using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D.Benchmark.Renderer;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Canvas;

/// <summary>Builds a fixed sixteen-path clip/gradient drawing without irrelevant rectangle parameters.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class PathCanvasBenchmarks
{
    /// <summary>Constructs the real curved paths, clip stack and shared gradient.</summary>
    /// <returns>The immutable drawing.</returns>
    [Benchmark]
    public DrawList2D ClippedGradientPaths() => DrawingWorkloads.ClippedGradientPaths();
}
