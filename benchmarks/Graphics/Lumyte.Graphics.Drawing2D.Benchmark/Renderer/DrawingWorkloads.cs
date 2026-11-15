using System.Numerics;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Renderer;

/// <summary>Bounded public-API recordings; no shader execution or pixels are produced.</summary>
public static class DrawingWorkloads
{
    /// <summary>Builds a fresh canvas and immutable rectangle snapshot.</summary>
    /// <param name="count">Rectangle count.</param>
    /// <returns>The completed recording.</returns>
    public static DrawList2D Rectangles(int count)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < count; i++)
        {
            canvas.FillRect(new((i % 32) * 12, (i / 32) * 12, 10, 10), new SRgba<float>(0.2f, 0.5f, 0.8f, 1));
        }

        return canvas.End();
    }

    /// <summary>Builds a bounded shared curved path, two clips and shared linear gradient profile.</summary>
    /// <returns>The completed 16-command recording.</returns>
    public static DrawList2D ClippedGradientPaths()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(4, 4));
        builder.CubicTo(new(28, 0), new(36, 24), new(24, 32));
        builder.LineTo(new(4, 28));
        builder.Close();
        Path2D path = builder.Build();
        var gradient = PaintGraph2D.LinearGradient(Vector2.Zero, new(32, 32), [new(0, new(1, 0, 0, 1)), new(0.5f, new(0, 1, 0, 0.75f)), new(1, new(0, 0, 1, 1))]);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(0, 0, 256, 256));
        for (int i = 0; i < 16; i++)
        {
            canvas.Save();
            canvas.Translate(new((i % 4) * 48, (i / 4) * 48));
            canvas.ClipPath(path);
            canvas.FillPath(path, gradient);
            canvas.Restore();
        }

        return canvas.End();
    }
}
