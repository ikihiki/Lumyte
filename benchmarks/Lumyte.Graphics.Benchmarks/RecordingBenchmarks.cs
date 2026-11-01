using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Benchmarks;

/// <summary>Pure CPU recording, excluding tessellation and GPU execution.</summary>
public class RecordingBenchmarks
{
    private readonly Canvas2D _canvas = new();
    private readonly PaintGraph2D _solid = PaintGraph2D.Solid(new(0.2f, 0.4f, 0.8f, 1));
    private readonly PaintGraph2D _gradient = PaintGraph2D.LinearGradient(Vector2.Zero, new(100, 0), [new(0, new(1, 0, 0, 1)), new(1, new(0, 0, 1, 1))]);
    private readonly Path2D _path = CreatePath();

    /// <summary>Gets or sets the command count, including the pool boundary.</summary>
    [Params(100, 1000, 1024, 1025, 10000)]
    public int Count { get; set; }

    /// <summary>Records reused solid paint and snapshots the tape.</summary>
    /// <returns>The immutable recording.</returns>
    [Benchmark(Baseline = true)]
    public DrawList2D Rectangles() => Record(false, false);

    /// <summary>Includes per-command convenience paint creation.</summary>
    /// <returns>The immutable recording.</returns>
    [Benchmark]
    public DrawList2D ColorRectangles() => Record(true, false);

    /// <summary>Records shared curved paths, gradient and clip.</summary>
    /// <returns>The immutable recording.</returns>
    [Benchmark]
    public DrawList2D PathsGradientClip() => Record(false, true);

    private static Path2D CreatePath()
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.CubicTo(new(0, 40), new(40, 40), new(40, 0));
        path.Close();
        return path.Build();
    }

    private DrawList2D Record(bool color, bool path)
    {
        _canvas.Begin();
        if (path)
        {
            _canvas.ClipRect(new(0, 0, 800, 800));
        }

        for (int i = 0; i < Count; i++)
        {
            if (path)
            {
                _canvas.FillPath(_path, _gradient);
            }
            else if (color)
            {
                _canvas.FillRect(new((i % 100) * 8, (i / 100) * 8, 7, 7), new Color2D(0.2f, 0.4f, 0.8f, 1));
            }
            else
            {
                _canvas.FillRect(new((i % 100) * 8, (i / 100) * 8, 7, 7), _solid);
            }
        }

        return _canvas.End();
    }
}
