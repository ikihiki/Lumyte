using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Benchmarks;

/// <summary>Isolates the renderer's curve flattening and round stroke CPU work.</summary>
public class PathPreparationBenchmarks
{
    private readonly Path2D _path = CreatePath();
    private readonly StrokeStyle2D _stroke = new() { Width = 8, Cap = LineCap2D.Round, Join = LineJoin2D.Round };

    /// <summary>Flattens an immutable cubic under a sheared scale and tessellates its stroke.</summary>
    /// <returns>The generated vertex count.</returns>
    [Benchmark]
    public int FlattenAndStroke()
    {
        var transform = new Matrix3x2(2, 0.2f, 0.4f, 3, 0, 0);
        return PathFlattening2D.Stroke(PathFlattening2D.Flatten(_path, transform), _stroke, transform).Count;
    }

    private static Path2D CreatePath()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.CubicTo(new(0, 80), new(80, -20), new(100, 40));
        builder.LineTo(new(50, 100));
        return builder.Build();
    }
}
