using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks geometric coverage and numerical contracts independently of the GPU shaders.</summary>
public sealed class RenderingGeometryTests
{
    /// <summary>Collinear control points may overshoot both endpoints and must not collapse into one chord.</summary>
    [Fact]
    public void CollinearBezierOvershootRetainsItsExtrema()
    {
        var quadratic = new PathBuilder2D();
        quadratic.MoveTo(Vector2.Zero);
        quadratic.QuadraticTo(new Vector2(100, 0), Vector2.UnitX);
        PathFlattening2D.Contour quadraticContour = Assert.Single(PathFlattening2D.Flatten(quadratic.Build(), Matrix3x2.Identity));
        Assert.InRange(quadraticContour.Points.Max(point => point.X), (10000f / 199) - 0.125f, (10000f / 199) + 0.125f);

        var cubic = new PathBuilder2D();
        cubic.MoveTo(Vector2.Zero);
        cubic.CubicTo(new Vector2(100, 0), new Vector2(-100, 0), Vector2.UnitX);
        PathFlattening2D.Contour cubicContour = Assert.Single(PathFlattening2D.Flatten(cubic.Build(), Matrix3x2.Identity));
        Assert.True(cubicContour.Points.Max(point => point.X) > 28);
        Assert.True(cubicContour.Points.Min(point => point.X) < -28);
        Assert.Equal(Vector2.UnitX, cubicContour.Points[^1]);
    }

    /// <summary>A segment after Close begins at the closed contour's start and remains an open subpath.</summary>
    [Fact]
    public void CloseThenCurveStartsAtTheClosedContourOrigin()
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(new Vector2(20, 0));
        path.LineTo(new Vector2(20, 20));
        path.Close();
        path.QuadraticTo(new Vector2(0, 20), new Vector2(20, 20));
        List<PathFlattening2D.Contour> contours = PathFlattening2D.Flatten(path.Build(), Matrix3x2.Identity);
        Assert.Equal(2, contours.Count);
        Assert.True(contours[0].Closed);
        Assert.False(contours[1].Closed);
        Assert.Equal(Vector2.Zero, contours[1].Points[0]);
        Assert.Equal(new Vector2(20, 20), contours[1].Points[^1]);
        Assert.Contains(contours[1].Points, point => point.X > 0 && point.X < 10 && point.Y > point.X);
    }

    /// <summary>Subdividing a curve uses output-pixel precision after rotation, shear, and nonuniform scale.</summary>
    [Fact]
    public void TransformedCubicStaysWithinItsPixelErrorBudget()
    {
        Vector2 start = new(0, 0);
        Vector2 control1 = new(0, 20);
        Vector2 control2 = new(30, -10);
        Vector2 end = new(40, 0);
        var transform = new Matrix3x2(40, 12, 17, 3, 5, -7);
        var path = new PathBuilder2D();
        path.MoveTo(start);
        path.CubicTo(control1, control2, end);
        PathFlattening2D.Contour contour = Assert.Single(PathFlattening2D.Flatten(path.Build(), transform));
        Vector2[] polyline = contour.Points.Select(point => Vector2.Transform(point, transform)).ToArray();
        for (int i = 0; i <= 1000; i++)
        {
            float t = i / 1000f;
            float u = 1 - t;
            Vector2 analytic = (start * u * u * u) + (control1 * 3 * u * u * t) + (control2 * 3 * u * t * t) + (end * t * t * t);
            Assert.InRange(DistanceToPolyline(Vector2.Transform(analytic, transform), polyline), 0, 0.126f);
        }
    }

    /// <summary>Cap geometry distinguishes flat, square, and round endpoint coverage.</summary>
    [Fact]
    public void StrokeCapsCoverTheirSpecifiedEndpointRegions()
    {
        List<PathFlattening2D.Contour> contours = Line(new Vector2(20, 20), new Vector2(40, 20));
        List<Vector4> butt = PathFlattening2D.Stroke(contours, new StrokeStyle2D { Width = 10, Cap = LineCap2D.Butt }, Matrix3x2.Identity);
        List<Vector4> square = PathFlattening2D.Stroke(contours, new StrokeStyle2D { Width = 10, Cap = LineCap2D.Square }, Matrix3x2.Identity);
        List<Vector4> round = PathFlattening2D.Stroke(contours, new StrokeStyle2D { Width = 10, Cap = LineCap2D.Round }, Matrix3x2.Identity);
        Assert.False(Contains(butt, new Vector2(18, 20)));
        Assert.True(Contains(square, new Vector2(18, 20)));
        Assert.True(Contains(round, new Vector2(18, 20)));
        Assert.True(Contains(square, new Vector2(16, 24)));
        Assert.False(Contains(round, new Vector2(16, 24)));
        Assert.True(Contains(butt, new Vector2(30, 24)));
        Assert.False(Contains(square, new Vector2(14, 20)));
    }

    /// <summary>A zero-length line preserves the selected cap's finite shape without inventing a tangent.</summary>
    [Fact]
    public void ZeroLengthLineUsesSquareOrRoundCapCoverage()
    {
        List<PathFlattening2D.Contour> contours = Line(Vector2.Zero, Vector2.Zero);
        List<Vector4> square = PathFlattening2D.Stroke(contours, new StrokeStyle2D { Width = 20, Cap = LineCap2D.Square }, Matrix3x2.Identity);
        List<Vector4> round = PathFlattening2D.Stroke(contours, new StrokeStyle2D { Width = 20, Cap = LineCap2D.Round }, Matrix3x2.Identity);
        List<Vector4> butt = PathFlattening2D.Stroke(contours, new StrokeStyle2D { Width = 20, Cap = LineCap2D.Butt }, Matrix3x2.Identity);
        Assert.Empty(butt);
        Assert.True(Contains(square, new Vector2(9, 9)));
        Assert.False(Contains(round, new Vector2(9, 9)));
        Assert.True(Contains(round, new Vector2(0, 9)));
        Assert.False(Contains(square, new Vector2(11, 0)));
        Assert.Equal(-10, square.Min(vertex => vertex.X));
        Assert.Equal(10, square.Max(vertex => vertex.X));
        Assert.Equal(-10, square.Min(vertex => vertex.Y));
        Assert.Equal(10, square.Max(vertex => vertex.Y));
    }

    /// <summary>Miter joins retain sharp outer corners until the miter limit selects a bevel.</summary>
    [Fact]
    public void StrokeJoinGeometryAndMiterLimitHaveDistinctCoverage()
    {
        var path = new PathBuilder2D();
        path.MoveTo(new Vector2(20, 20));
        path.LineTo(new Vector2(30, 20));
        path.LineTo(new Vector2(30, 30));
        List<PathFlattening2D.Contour> contours = PathFlattening2D.Flatten(path.Build(), Matrix3x2.Identity);
        var style = new StrokeStyle2D { Width = 10, Join = LineJoin2D.Miter, MiterLimit = 2 };
        List<Vector4> miter = PathFlattening2D.Stroke(contours, style, Matrix3x2.Identity);
        List<Vector4> limited = PathFlattening2D.Stroke(contours, style with { MiterLimit = 1 }, Matrix3x2.Identity);
        List<Vector4> bevel = PathFlattening2D.Stroke(contours, style with { Join = LineJoin2D.Bevel }, Matrix3x2.Identity);
        List<Vector4> round = PathFlattening2D.Stroke(contours, style with { Join = LineJoin2D.Round }, Matrix3x2.Identity);
        Vector2 sharpCorner = new(34.5f, 15.5f);
        Assert.True(Contains(miter, sharpCorner));
        Assert.False(Contains(limited, sharpCorner));
        Assert.False(Contains(bevel, sharpCorner));
        Assert.False(Contains(round, sharpCorner));
        Vector2 roundedCorner = new(33.1f, 16.9f);
        Assert.True(Contains(round, roundedCorner));
        Assert.False(Contains(bevel, roundedCorner));
    }

    /// <summary>Round tessellation accounts for the maximum stretch of a sheared transform.</summary>
    [Fact]
    public void ShearedRoundCapStaysWithinItsPixelErrorBudget()
    {
        var transform = new Matrix3x2(1, 0, 1, 1, 0, 0);
        List<Vector4> triangles = PathFlattening2D.Stroke(Line(Vector2.Zero, Vector2.Zero), new StrokeStyle2D { Width = 12.2f, Cap = LineCap2D.Round }, transform);
        Vector2[] boundary = triangles.Where((_, index) => index % 3 == 1).Select(vertex => Vector2.Transform(new Vector2(vertex.X, vertex.Y), transform)).ToArray();
        boundary = [.. boundary, boundary[0]];
        for (int i = 0; i < 1024; i++)
        {
            float angle = MathF.Tau * i / 1024;
            var analytic = Vector2.Transform(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 6.1f, transform);
            Assert.InRange(DistanceToPolyline(analytic, boundary), 0, 0.126f);
        }
    }

    /// <summary>Unrepresentably large arc subdivision requirements fail explicitly instead of wrapping to a tiny polygon.</summary>
    [Fact]
    public void EnormousRoundStrokeRejectsItsSubdivisionBudget() => Assert.Throws<NotSupportedException>(() => PathFlattening2D.Stroke(Line(Vector2.Zero, Vector2.One), new StrokeStyle2D { Width = 1e30f, Cap = LineCap2D.Round }, Matrix3x2.Identity));

    /// <summary>Square-cap extrema remain inside the scissor after the Canvas transform, scale, and target origin.</summary>
    /// <param name="transformed">Whether to include a nontrivial Canvas transform.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SquareCapBoundsRemainConservativeAfterTransform(bool transformed)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (transformed)
        {
            canvas.ConcatTransform(new Matrix3x2(1, 0.3f, 0.6f, 1, 20, 10));
        }

        var style = new StrokeStyle2D { Width = 20, Cap = LineCap2D.Square, MiterLimit = 1 };
        canvas.DrawLine(new Vector2(30, 30), new Vector2(70, 70), new Color2D(1, 1, 1, 1), style);
        var path = new PathBuilder2D();
        path.MoveTo(new Vector2(30, 30));
        path.LineTo(new Vector2(70, 70));
        canvas.StrokePath(path.Build(), new Color2D(1, 1, 1, 1), style);
        DrawList2D draws = canvas.End();
        RenderTargetBinding2D target = Target() with { PixelScale = 2, PixelOrigin = new Vector2(5, 10) };
        float extent = 10 * MathF.Sqrt(2);
        Vector2[] corners = [new(30 - extent, 30), new(30, 30 - extent), new(70 + extent, 70), new(70, 70 + extent)];
        foreach (DrawingCommand2D command in draws.Commands)
        {
            ScissorRect bounds = DrawingBounds2D.GetScissor(command, null, target, null, null, false);
            foreach (Vector2 corner in corners)
            {
                Vector2 pixel = (Vector2.Transform(corner, command.State.Transform) * target.PixelScale) - target.PixelOrigin;
                Assert.InRange(pixel.X, bounds.X, bounds.X + bounds.Width);
                Assert.InRange(pixel.Y, bounds.Y, bounds.Y + bounds.Height);
            }
        }
    }

    /// <summary>Repeated shared paint children count their real evaluation cost against the finite shader budget.</summary>
    [Fact]
    public void SharedPaintDagExpansionFailsBeforeExceedingShaderCapacity()
    {
        var paint = PaintGraph2D.Solid(new Color2D(1, 0, 0, 1));
        for (int i = 0; i < 8; i++)
        {
            paint = PaintGraph2D.Composite(paint, paint, CompositeMode2D.SrcOver);
        }

        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawPaintGraph(paint);
        DrawingCommand2D command = Assert.Single(canvas.End().Commands);
        using var table = new RejectingArgumentTable();
        var compiler = new DrawingProgram2D(table, new UnusedSamplerReference(), Target());
        Assert.Throws<NotSupportedException>(() => compiler.Compile(command));
        Assert.InRange(compiler.Instructions.Count, 1, DrawingProgram2D.MaximumInstructions);
    }

    private static RenderTargetBinding2D Target()
    {
        var description = new TextureDesc { Width = 512, Height = 512, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment };
        var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(new DispatchDevice());
        return new RenderTargetBinding2D { Texture = graph.CreateTexture(description), Description = description };
    }

    private static List<PathFlattening2D.Contour> Line(Vector2 start, Vector2 end)
    {
        var path = new PathBuilder2D();
        path.MoveTo(start);
        path.LineTo(end);
        return PathFlattening2D.Flatten(path.Build(), Matrix3x2.Identity);
    }

    private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> polyline)
    {
        float minimum = float.PositiveInfinity;
        for (int i = 1; i < polyline.Count; i++)
        {
            Vector2 start = polyline[i - 1];
            Vector2 delta = polyline[i] - start;
            float lengthSquared = delta.LengthSquared();
            float t = lengthSquared == 0 ? 0 : Math.Clamp(Vector2.Dot(point - start, delta) / lengthSquared, 0, 1);
            minimum = MathF.Min(minimum, Vector2.Distance(point, start + (delta * t)));
        }

        return minimum;
    }

    private static bool Contains(IReadOnlyList<Vector4> triangles, Vector2 point)
    {
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector2 a = new(triangles[i].X, triangles[i].Y);
            Vector2 b = new(triangles[i + 1].X, triangles[i + 1].Y);
            Vector2 c = new(triangles[i + 2].X, triangles[i + 2].Y);
            float ab = Cross(b - a, point - a);
            float bc = Cross(c - b, point - b);
            float ca = Cross(a - c, point - c);
            if ((ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0))
            {
                return true;
            }
        }

        return false;
    }

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

    private sealed class UnusedSamplerReference : IGpuRef<IGraphicsSampler>
    {
        public ulong Count => 1;

        public IGpuRef<IGraphicsSampler> GetElement(ulong index) => throw new InvalidOperationException("Solid paint compilation must not use sampler references.");
    }

    private sealed class RejectingArgumentTable : IArgumentTable
    {
        public uint TextureCapacity => 0;

        public uint SamplerCapacity => 0;

        public uint BufferCapacity => 0;

        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => throw new InvalidOperationException("Solid paint must not bind textures.");

        public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => throw new InvalidOperationException("Solid paint must not bind samplers.");

        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
            where T : unmanaged => throw new InvalidOperationException("Paint compilation must not bind buffers.");

        public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
            where T : struct, IShaderData => throw new InvalidOperationException("Paint compilation must not bind buffers.");

        public void ReleaseTexture(uint slot) => throw new InvalidOperationException("Paint compilation must not release textures.");

        public void ReleaseSampler(uint slot) => throw new InvalidOperationException("Paint compilation must not release samplers.");

        public void ReleaseBuffer(uint slot) => throw new InvalidOperationException("Paint compilation must not release buffers.");

        public void Dispose()
        {
        }
    }
}
