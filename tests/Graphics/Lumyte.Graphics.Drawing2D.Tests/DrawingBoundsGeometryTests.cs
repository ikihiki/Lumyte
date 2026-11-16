using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks transformed bounds against the former corner enumeration.</summary>
public sealed class DrawingBoundsGeometryTests
{
    /// <summary>Rotation, reflection, shear, inverted and degenerate rectangles retain the corner oracle.</summary>
    [Fact]
    public void TransformedBoundsMatchCornerOracle()
    {
        var random = new Random(9417);
        for (int i = 0; i < 1000; i++)
        {
            Vector2 first = new(Next(), Next());
            Vector2 last = i % 5 == 0 ? first : new(Next(), Next());
            var transform = new Matrix3x2(Next(), Next(), Next(), Next(), Next(), Next());
            (Vector2 lower, Vector2 upper) = Reference(first, last, transform);
            Geometry2D.GetTransformedBoundsBcl(first, last, transform, out Vector2 actualLower, out Vector2 actualUpper);
            Assert.Equal(lower, actualLower);
            Assert.Equal(upper, actualUpper);
        }

        float Next() => (random.NextSingle() - 0.5f) * 100;
    }

    /// <summary>Scissors preserve transform composition, extent reconstruction and one-pixel padding.</summary>
    [Fact]
    public void ScissorsMatchOldCornerReduction()
    {
        Matrix3x2[] transforms = [Matrix3x2.Identity, Matrix3x2.CreateRotation(0.63f), new(-2, 0.4f, 0.7f, 3, 40, 20), Matrix3x2.CreateScale(0)];
        Rect2D[] rectangles = [new(10, 20, 30, 40), new(50, 60, -30, -40), new(25, 25, 0, 0)];
        DrawingTargetInfo2D target = Target() with { PixelScale = 1.5f, PixelOrigin = new(3, 5) };
        foreach (Matrix3x2 transform in transforms)
        {
            foreach (Rect2D rect in rectangles)
            {
                var command = new FillRectCommand2D(rect, PaintGraph2D.Solid(new(1, 1, 1, 1)), new(transform, ClipStack2D.Empty));
                Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
                Assert.Equal(ReferenceScissor(rect, transform * pixels, target), DrawingBounds2D.GetScissor(command, null, target, null, false, false));
            }
        }
    }

    /// <summary>Finite corners whose difference overflows remain unsupported.</summary>
    [Fact]
    public void OverflowingExtentIsRejected()
    {
        var command = new FillRectCommand2D(new(-1, 0, 2, 1), PaintGraph2D.Solid(new(1, 1, 1, 1)), new(Matrix3x2.CreateScale(float.MaxValue), ClipStack2D.Empty));
        Assert.Throws<NotSupportedException>(() => DrawingBounds2D.GetScissor(command, null, Target(), null, false, false));
    }

    /// <summary>Non-finite transforms retain the former BCL corner reduction behavior.</summary>
    [Fact]
    public void NonFiniteCornersMatchOracle()
    {
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var transform = new Matrix3x2(value, 0, 0, 1, 0, 0);
            (Vector2 lower, Vector2 upper) = Reference(Vector2.Zero, Vector2.One, transform);
            Geometry2D.GetTransformedBoundsBcl(Vector2.Zero, Vector2.One, transform, out Vector2 actualLower, out Vector2 actualUpper);
            Assert.Equal(lower.X, actualLower.X);
            Assert.Equal(lower.Y, actualLower.Y);
            Assert.Equal(upper.X, actualUpper.X);
            Assert.Equal(upper.Y, actualUpper.Y);
        }
    }

    /// <summary>Captured clip paths use their own transform and the same padded bounds.</summary>
    [Fact]
    public void GeometricClipBoundsRetainPadding()
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(10, 20));
        path.LineTo(new(30, 20));
        path.LineTo(new(30, 40));
        path.Close();
        var transform = Matrix3x2.CreateTranslation(5, 7);
        DrawingState2D state = new(Matrix3x2.Identity, new ClipStack2D([new(path.Build(), transform, FillRule2D.NonZero)]));
        Assert.Equal(new ScissorRect(14, 26, 22, 22), DrawingBounds2D.GetScissor(null, state, Target(), null, false, false));
    }

    /// <summary>Square caps and miter outsets retain the existing conservative stroke policy.</summary>
    [Fact]
    public void StrokeOutsetsRemainConservative()
    {
        DrawingState2D state = new(Matrix3x2.Identity, ClipStack2D.Empty);
        var paint = PaintGraph2D.Solid(new(1, 1, 1, 1));
        var style = new StrokeStyle2D { Width = 10, Cap = LineCap2D.Square, MiterLimit = 4 };
        var line = new DrawLineCommand2D(new(30, 40), new(50, 60), paint, style, state);
        float amount = 10 * 0.707107f;
        Rect2D lineBounds = new(30 - amount, 40 - amount, 20 + (amount * 2), 20 + (amount * 2));
        Assert.Equal(ReferenceScissor(lineBounds, Matrix3x2.Identity, Target()), DrawingBounds2D.GetScissor(line, null, Target(), null, false, false));
        var path = new PathBuilder2D();
        path.MoveTo(new(30, 40));
        path.CubicTo(new(70, 20), new(10, 80), new(50, 60));
        path.Close();
        var stroke = new StrokePathCommand2D(path.Build(), paint, style, state);
        Assert.Equal(ReferenceScissor(new(-10, 0, 100, 100), Matrix3x2.Identity, Target()), DrawingBounds2D.GetScissor(stroke, null, Target(), null, false, false));
    }

    /// <summary>Normalized clips remain exact and source rectangles keep their existing origin-only transform.</summary>
    [Fact]
    public void SourceAndNormalizedClipPoliciesAreUnchanged()
    {
        DrawingTargetInfo2D target = Target() with { PixelOrigin = new(5, 7), PixelScale = 2 };
        Rect2D source = new(20, 30, 40, 50);
        Assert.Equal(ReferenceScissor(source, Matrix3x2.CreateTranslation(-target.PixelOrigin), target), DrawingBounds2D.GetScissor(null, null, target, source, false, false));
        Assert.Equal(new ScissorRect(3, 4, 5, 6), DrawingBounds2D.GetScissor(null, null, target, null, false, false, new(3, 4, 5, 6)));
        Assert.Equal(new ScissorRect(0, 0, 256, 256), DrawingBounds2D.GetScissor(null, null, target, source, true, false));
        Assert.Equal(new ScissorRect(0, 0, 256, 256), DrawingBounds2D.GetScissor(null, null, target, source, false, true));
    }

    private static (Vector2 Minimum, Vector2 Maximum) Reference(Vector2 first, Vector2 last, Matrix3x2 transform)
    {
        Vector2[] corners = [first, new(last.X, first.Y), new(first.X, last.Y), last];
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        foreach (Vector2 point in corners.Select(point => Vector2.Transform(point, transform)))
        {
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        return (minimum, maximum);
    }

    private static ScissorRect ReferenceScissor(Rect2D rect, Matrix3x2 transform, DrawingTargetInfo2D target)
    {
        (Vector2 minimum, Vector2 maximum) = Reference(new(rect.X, rect.Y), new(rect.X + rect.Width, rect.Y + rect.Height), transform);
        Vector2 extent = maximum - minimum;
        Vector2 upper = minimum + extent + Vector2.One;
        minimum -= Vector2.One;
        uint x = (uint)Math.Clamp(MathF.Floor(minimum.X), 0, target.Description.Width);
        uint y = (uint)Math.Clamp(MathF.Floor(minimum.Y), 0, target.Description.Height);
        uint right = (uint)Math.Clamp(MathF.Ceiling(upper.X), x, target.Description.Width);
        uint bottom = (uint)Math.Clamp(MathF.Ceiling(upper.Y), y, target.Description.Height);
        return new(x, y, right - x, bottom - y);
    }

    private static DrawingTargetInfo2D Target() => new() { Description = new() { Width = 256, Height = 256, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
}
