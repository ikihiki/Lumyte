using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks consolidation against the pre-consolidation single-precision renderer.</summary>
/// <param name="output">Receives allocation comparison measurements.</param>
public sealed class DrawingGeometryIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>Random curves and transforms produce bit-identical ordered points and stroke triangles.</summary>
    [Fact]
    public void FlatteningAndStrokeMatchHistoricalOracle()
    {
        var random = new Random(17417);
        for (int i = 0; i < 150; i++)
        {
            var builder = new PathBuilder2D();
            builder.MoveTo(Point());
            builder.QuadraticTo(Point(), Point());
            builder.CubicTo(Point(), Point(), Point());
            builder.Close();
            builder.LineTo(Point());
            Path2D path = builder.Build();
            Matrix3x2 transform = new(Next(), Next(), Next(), Next(), Next(), Next());
            List<PathFlattening2D.Contour> actual = PathFlattening2D.Flatten(path, transform);
            List<LegacyDrawingGeometryOracle.Contour> expected = LegacyDrawingGeometryOracle.Flatten(path, transform);
            Assert.Equal(expected.Count, actual.Count);
            for (int j = 0; j < actual.Count; j++)
            {
                Assert.Equal(expected[j].Closed, actual[j].Closed);
                Assert.Equal(expected[j].Points, actual[j].Points);
            }

            var style = new StrokeStyle2D { Width = 3, Cap = (LineCap2D)(i % 3), Join = (LineJoin2D)(i % 3) };
            Assert.Equal(LegacyDrawingGeometryOracle.Stroke(expected, style, transform), PathFlattening2D.Stroke(actual, style, transform));
        }

        float Next() => (random.NextSingle() - 0.5f) * 20;
        Vector2 Point() => new(Next(), Next());
    }

    /// <summary>Global point limits and precision failures retain their exception messages.</summary>
    [Fact]
    public void ResourceBudgetsRemainUnchanged()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        for (int i = 0; i < 65536; i++)
        {
            builder.LineTo(Vector2.One);
        }

        Path2D path = builder.Build();
        Assert.Equal(Assert.Throws<NotSupportedException>(() => LegacyDrawingGeometryOracle.Flatten(path, Matrix3x2.Identity)).Message, Assert.Throws<NotSupportedException>(() => PathFlattening2D.Flatten(path, Matrix3x2.Identity)).Message);
        var extreme = new Path2D([new(PathVerb2D.Move, Vector2.Zero, default, default), new(PathVerb2D.Quadratic, new(float.MaxValue), Vector2.Zero, default)]);
        Assert.Equal(Assert.Throws<NotSupportedException>(() => LegacyDrawingGeometryOracle.Flatten(extreme, Matrix3x2.CreateScale(float.MaxValue))).Message, Assert.Throws<NotSupportedException>(() => PathFlattening2D.Flatten(extreme, Matrix3x2.CreateScale(float.MaxValue))).Message);
    }

    /// <summary>Streaming helpers allocate no per-curve buffers and preserve callback failure semantics.</summary>
    [Fact]
    public void StreamingFlatteningAllocatesNoBuffers()
    {
        Action<Vector2> append = static _ => { };
        for (int i = 0; i < 1000; i++)
        {
            DrawingGeometry.AppendFlattenedCubic(Vector2.Zero, Vector2.One, new(2, -1), new(3, 0), 0.125f, append, 24);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            DrawingGeometry.AppendFlattenedCubic(Vector2.Zero, Vector2.One, new(2, -1), new(3, 0), 0.125f, append, 24);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        var failure = new InvalidOperationException();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => DrawingGeometry.AppendFlattenedQuadratic(Vector2.Zero, Vector2.One, new(2, 0), 0.125f, _ => throw failure, 24)));
        Assert.Throws<NotSupportedException>(() => DrawingGeometry.AppendFlattenedQuadratic(Vector2.Zero, Vector2.One, new(2, 0), 0.125f, append, 0));
    }

    /// <summary>Path, line and mesh bounds no longer allocate point gathers or LINQ iterators.</summary>
    [Fact]
    public void GatherBoundsAllocateNothing()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(20, 20));
        builder.CubicTo(new(80, 10), new(10, 80), new(40, 40));
        DrawingState2D state = new(Matrix3x2.Identity, ClipStack2D.Empty);
        var paint = PaintGraph2D.Solid(new(1, 1, 1, 1));
        DrawingCommand2D[] commands = [new FillPathCommand2D(builder.Build(), paint, FillRule2D.NonZero, state), new DrawLineCommand2D(new(10), new(30), paint, new(), state), new DrawMeshCommand2D(new Mesh2D([new(new(10), 1), new(new(30), 1), new(new(50), 1)], [0, 1, 2]), paint, state)];
        var target = new DrawingTargetInfo2D { Description = new() { Width = 256, Height = 256, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        foreach (DrawingCommand2D command in commands)
        {
            Assert.Equal(LegacyDrawingBoundsOracle.GetScissor(command, null, target, null, false, false), DrawingBounds2D.GetScissor(command, null, target, null, false, false));
            for (int i = 0; i < 1000; i++)
            {
                _ = DrawingBounds2D.GetScissor(command, null, target, null, false, false);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                _ = DrawingBounds2D.GetScissor(command, null, target, null, false, false);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                _ = LegacyDrawingBoundsOracle.GetScissor(command, null, target, null, false, false);
            }

            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before > 0);
        }
    }

    /// <summary>Control hulls, miter/cap outsets and transformed bounds match the former gather reduction.</summary>
    [Fact]
    public void ConservativeBoundsMatchHistoricalOracle()
    {
        var random = new Random(419);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 256, Height = 256, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment }, PixelScale = 1.5f, PixelOrigin = new(3, 5) };
        var paint = PaintGraph2D.Solid(new(1, 1, 1, 1));
        for (int i = 0; i < 200; i++)
        {
            var builder = new PathBuilder2D();
            builder.MoveTo(Point());
            builder.QuadraticTo(Point(), Point());
            builder.CubicTo(Point(), Point(), Point());
            builder.Close();
            DrawingState2D state = new(new(Next(), Next(), Next(), Next(), Next(), Next()), ClipStack2D.Empty);
            var style = new StrokeStyle2D { Width = 5, Cap = (LineCap2D)(i % 3), MiterLimit = 4 };
            DrawingCommand2D[] commands = [new FillPathCommand2D(builder.Build(), paint, FillRule2D.NonZero, state), new StrokePathCommand2D(builder.Build(), paint, style, state), new DrawLineCommand2D(Point(), Point(), paint, style, state), new DrawMeshCommand2D(new Mesh2D([new(Point(), 1), new(Point(), 1), new(Point(), 1)], [0, 1, 2]), paint, state)];
            foreach (DrawingCommand2D command in commands)
            {
                Assert.Equal(LegacyDrawingBoundsOracle.GetScissor(command, null, target, null, false, false), DrawingBounds2D.GetScissor(command, null, target, null, false, false));
            }
        }

        float Next() => (random.NextSingle() - 0.5f) * 20;
        Vector2 Point() => new(Next(), Next());
    }

    /// <summary>Both line-only and curved paths stay within pre-consolidation warmed allocation budgets.</summary>
    /// <param name="curves">Whether the path contains curves.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlatteningDoesNotAddAllocations(bool curves)
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        if (curves)
        {
            builder.QuadraticTo(new(1, 2), new(3, 0));
            builder.CubicTo(new(4, -2), new(5, 2), new(6, 0));
        }
        else
        {
            builder.LineTo(Vector2.One);
        }

        Path2D path = builder.Build();
        for (int i = 0; i < 1000; i++)
        {
            _ = PathFlattening2D.Flatten(path, Matrix3x2.Identity);
            _ = LegacyDrawingGeometryOracle.Flatten(path, Matrix3x2.Identity);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = LegacyDrawingGeometryOracle.Flatten(path, Matrix3x2.Identity);
        }

        long historical = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = PathFlattening2D.Flatten(path, Matrix3x2.Identity);
        }

        long actual = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"curves={curves}: historical={historical / 1000} B/path, actual={actual / 1000} B/path");
        Assert.True(actual <= historical, $"curves={curves}: historical={historical / 1000} B/path, actual={actual / 1000} B/path");
    }

    private static class LegacyDrawingGeometryOracle
    {
        internal const int MaximumEdges = 65536;

        internal static List<Contour> Flatten(Path2D path, Matrix3x2 transform)
        {
            List<Contour> result = [];
            Contour? current = null;
            Vector2 position = default;
            float scale = MaximumScale(transform);
            float tolerance = 0.125f / MathF.Max(scale, 0.0001f);
            int count = 0;
            foreach (PathSegment2D segment in path.Segments)
            {
                if (current is { Closed: true } && segment.Verb is not PathVerb2D.Move and not PathVerb2D.Close)
                {
                    current = new Contour();
                    result.Add(current);
                    Add(position);
                }

                switch (segment.Verb)
                {
                    case PathVerb2D.Move:
                        current = new Contour();
                        result.Add(current);
                        Add(segment.P0);
                        break;
                    case PathVerb2D.Line:
                        Add(segment.P0);
                        break;
                    case PathVerb2D.Quadratic:
                        Quadratic(position, segment.P0, segment.P1, 0);
                        break;
                    case PathVerb2D.Cubic:
                        Cubic(position, segment.P0, segment.P1, segment.P2, 0);
                        break;
                    case PathVerb2D.Close:
                        current!.Closed = true;
                        position = current.Points[0];
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(path));
                }
            }

            return result;

            void Add(Vector2 point)
            {
                if (++count > MaximumEdges)
                {
                    throw new NotSupportedException("Path flattening exceeds the 65536 edge budget.");
                }

                current!.Points.Add(point);
                position = point;
            }

            void Quadratic(Vector2 p0, Vector2 p1, Vector2 p2, int depth)
            {
                if (Distance(p1, p0, p2) <= tolerance)
                {
                    Add(p2);
                    return;
                }

                if (depth >= 24)
                {
                    throw new NotSupportedException("Curve subdivision exceeds the precision budget.");
                }

                Vector2 a = (p0 + p1) * 0.5f;
                Vector2 b = (p1 + p2) * 0.5f;
                Vector2 midpoint = (a + b) * 0.5f;
                Quadratic(p0, a, midpoint, depth + 1);
                Quadratic(midpoint, b, p2, depth + 1);
            }

            void Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int depth)
            {
                if (MathF.Max(Distance(p1, p0, p3), Distance(p2, p0, p3)) <= tolerance)
                {
                    Add(p3);
                    return;
                }

                if (depth >= 24)
                {
                    throw new NotSupportedException("Curve subdivision exceeds the precision budget.");
                }

                Vector2 a = (p0 + p1) * 0.5f;
                Vector2 b = (p1 + p2) * 0.5f;
                Vector2 c = (p2 + p3) * 0.5f;
                Vector2 d = (a + b) * 0.5f;
                Vector2 e = (b + c) * 0.5f;
                Vector2 f = (d + e) * 0.5f;
                Cubic(p0, a, d, f, depth + 1);
                Cubic(f, e, c, p3, depth + 1);
            }
        }

        internal static List<Vector4> Stroke(List<Contour> contours, StrokeStyle2D style, Matrix3x2 transform)
        {
            List<Vector4> triangles = [];
            float half = style.Width * 0.5f;
            float scale = MaximumScale(transform);
            int divisions = 8;
            if (style.Cap == LineCap2D.Round || style.Join == LineJoin2D.Round)
            {
                double requiredDivisions = Math.Ceiling(Math.PI / Math.Acos(Math.Clamp(1 - (0.125 / Math.Max((double)half * scale, 0.125)), -1, 1)));
                if (!double.IsFinite(requiredDivisions) || requiredDivisions > 4096)
                {
                    throw new NotSupportedException("Stroke round join exceeds the subdivision budget.");
                }

                divisions = Math.Max(8, (int)requiredDivisions);
            }

            foreach (Contour contour in contours)
            {
                List<Vector2> points = [];
                foreach (Vector2 point in contour.Points)
                {
                    if (points.Count == 0 || point != points[^1])
                    {
                        points.Add(point);
                    }
                }

                if (contour.Closed && points.Count > 1 && points[0] == points[^1])
                {
                    points.RemoveAt(points.Count - 1);
                }

                if (points.Count < 2)
                {
                    if (points.Count == 1 && style.Cap == LineCap2D.Round)
                    {
                        Disc(points[0]);
                    }
                    else if (points.Count == 1 && style.Cap == LineCap2D.Square)
                    {
                        Vector2 center = points[0];
                        Triangle(center + new Vector2(-half, -half), center + new Vector2(half, -half), center + new Vector2(half, half));
                        Triangle(center + new Vector2(-half, -half), center + new Vector2(half, half), center + new Vector2(-half, half));
                    }

                    continue;
                }

                int segments = contour.Closed ? points.Count : points.Count - 1;
                for (int i = 0; i < segments; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[(i + 1) % points.Count];
                    var direction = Vector2.Normalize(b - a);
                    Vector2 normal = new Vector2(-direction.Y, direction.X) * half;
                    if (!contour.Closed && style.Cap == LineCap2D.Square)
                    {
                        if (i == 0)
                        {
                            a -= direction * half;
                        }

                        if (i == segments - 1)
                        {
                            b += direction * half;
                        }
                    }

                    Triangle(a + normal, a - normal, b + normal);
                    Triangle(b + normal, a - normal, b - normal);
                }

                for (int i = contour.Closed ? 0 : 1; i < (contour.Closed ? points.Count : points.Count - 1); i++)
                {
                    Vector2 center = points[i];
                    var before = Vector2.Normalize(center - points[(i + points.Count - 1) % points.Count]);
                    var after = Vector2.Normalize(points[(i + 1) % points.Count] - center);
                    if (style.Join == LineJoin2D.Round)
                    {
                        Disc(center);
                        continue;
                    }

                    float turn = Cross(before, after);
                    if (MathF.Abs(turn) < 0.000001f)
                    {
                        continue;
                    }

                    float sign = turn > 0 ? -1 : 1;
                    Vector2 n0 = new Vector2(-before.Y, before.X) * (half * sign);
                    Vector2 n1 = new Vector2(-after.Y, after.X) * (half * sign);
                    Vector2 a = center + n0;
                    Vector2 b = center + n1;
                    if (style.Join == LineJoin2D.Miter)
                    {
                        Vector2 miter = a + (before * (Cross(b - a, after) / turn));
                        if (Vector2.Distance(miter, center) <= style.MiterLimit * half)
                        {
                            Triangle(a, miter, center);
                            Triangle(miter, b, center);
                            continue;
                        }
                    }

                    Triangle(a, b, center);
                }

                if (!contour.Closed && style.Cap == LineCap2D.Round)
                {
                    Disc(points[0]);
                    Disc(points[^1]);
                }
            }

            return triangles;

            void Triangle(Vector2 a, Vector2 b, Vector2 c)
            {
                if (triangles.Count >= MaximumEdges * 3)
                {
                    throw new NotSupportedException("Stroke exceeds the triangle budget.");
                }

                triangles.Add(new(a, 1, 0));
                triangles.Add(new(b, 1, 0));
                triangles.Add(new(c, 1, 0));
            }

            void Disc(Vector2 center)
            {
                for (int i = 0; i < divisions; i++)
                {
                    float a = MathF.Tau * i / divisions;
                    float b = MathF.Tau * (i + 1) / divisions;
                    Triangle(center, center + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * half), center + (new Vector2(MathF.Cos(b), MathF.Sin(b)) * half));
                }
            }
        }

        private static float MaximumScale(Matrix3x2 transform)
        {
            double squared = ((double)transform.M11 * transform.M11) + ((double)transform.M12 * transform.M12) + ((double)transform.M21 * transform.M21) + ((double)transform.M22 * transform.M22);
            return (float)Math.Min(float.MaxValue, Math.Sqrt(squared));
        }

        private static float Cross(Vector2 a, Vector2 b) => Vector2.Cross(a, b);

        private static float Distance(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            float lengthSquared = delta.LengthSquared();
            if (lengthSquared == 0)
            {
                return Vector2.Distance(point, start);
            }

            float projection = Math.Clamp(Vector2.Dot(point - start, delta) / lengthSquared, 0, 1);
            return Vector2.Distance(point, start + (projection * delta));
        }

        internal sealed class Contour
        {
            internal List<Vector2> Points { get; } = [];

            internal bool Closed { get; set; }
        }
    }

    private static class LegacyDrawingBoundsOracle
    {
        internal static ScissorRect GetScissor(DrawingCommand2D? command, DrawingState2D? clip, DrawingTargetInfo2D target, Rect2D? source, bool hasOriginal, bool replace, ScissorRect? normalized = null)
        {
            Vector2 minimum = Vector2.Zero;
            Vector2 maximum = new(target.Description.Width, target.Description.Height);
            Matrix3x2 pixels = Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
            DrawingState2D? state = command?.State ?? clip;
            if (command is not null)
            {
                Rect2D? bounds = command switch
                {
                    FillRectCommand2D c => c.Rect,
                    FillRoundedRectCommand2D c => c.Rect,
                    FillCircleCommand2D c => new(c.Center.X - c.Radius, c.Center.Y - c.Radius, c.Radius * 2, c.Radius * 2),
                    DrawLineCommand2D c => Outset(Points([c.Start, c.End]), c.Style.Width * (c.Style.Cap == LineCap2D.Square ? 0.707107f : 0.5f)),
                    FillPathCommand2D c => PathBounds(c.Path),
                    StrokePathCommand2D c => Outset(PathBounds(c.Path), c.Style.Width * MathF.Max(c.Style.Cap == LineCap2D.Square ? 1.414214f : 1, c.Style.MiterLimit) * 0.5f),
                    DrawImageCommand2D c => c.Destination,
                    DrawCoverageMaskCommand2D c => c.Destination,
                    DrawDistanceFieldCommand2D c => c.Destination,
                    DrawMeshCommand2D c => Points(c.Mesh.Vertices.Select(v => v.Position)),
                    _ => null,
                };
                if (bounds is not null)
                {
                    Intersect(bounds.Value, command.State.Transform * pixels);
                }
            }
            else if (source is not null && !replace && !hasOriginal)
            {
                Intersect(source.Value, Matrix3x2.CreateTranslation(-target.PixelOrigin));
            }

            if (normalized is ScissorRect exact)
            {
                minimum = Vector2.Max(minimum, new(exact.X, exact.Y));
                maximum = Vector2.Min(maximum, new(exact.X + exact.Width, exact.Y + exact.Height));
            }
            else if (state is not null)
            {
                foreach (ClipElement2D element in state.Clip.Elements)
                {
                    Intersect(PathBounds(element.Path), element.Transform * pixels);
                }
            }

            uint x = (uint)Math.Clamp(MathF.Floor(minimum.X), 0, target.Description.Width);
            uint y = (uint)Math.Clamp(MathF.Floor(minimum.Y), 0, target.Description.Height);
            uint right = (uint)Math.Clamp(MathF.Ceiling(maximum.X), x, target.Description.Width);
            uint bottom = (uint)Math.Clamp(MathF.Ceiling(maximum.Y), y, target.Description.Height);
            return new(x, y, right - x, bottom - y);

            void Intersect(Rect2D bounds, Matrix3x2 transform)
            {
                NumericsGeometry.GetTransformedBounds(new(bounds.X, bounds.Y), new(bounds.X + bounds.Width, bounds.Y + bounds.Height), transform, out Vector2 lower, out Vector2 upper);

                // Retain the previous extent reconstruction and its finite-range check.
                Rect2D transformed = float.IsPositiveInfinity(lower.X) ? default : new(lower.X, lower.Y, upper.X - lower.X, upper.Y - lower.Y);
                if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y) || !float.IsFinite(transformed.Width) || !float.IsFinite(transformed.Height))
                {
                    throw new NotSupportedException("Transformed drawing bounds exceed the finite coordinate range.");
                }

                minimum = Vector2.Max(minimum, new(transformed.X - 1, transformed.Y - 1));
                maximum = Vector2.Min(maximum, new(transformed.X + transformed.Width + 1, transformed.Y + transformed.Height + 1));
            }
        }

        private static Rect2D PathBounds(Path2D path)
        {
            List<Vector2> points = [];
            foreach (PathSegment2D segment in path.Segments)
            {
                if (segment.Verb != PathVerb2D.Close)
                {
                    points.Add(segment.P0);
                }

                if (segment.Verb is PathVerb2D.Quadratic or PathVerb2D.Cubic)
                {
                    points.Add(segment.P1);
                }

                if (segment.Verb == PathVerb2D.Cubic)
                {
                    points.Add(segment.P2);
                }
            }

            return Points(points);
        }

        private static Rect2D Points(IEnumerable<Vector2> points)
        {
            Vector2 minimum = new(float.PositiveInfinity);
            Vector2 maximum = new(float.NegativeInfinity);
            foreach (Vector2 point in points)
            {
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }

            return float.IsPositiveInfinity(minimum.X) ? default : new(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y);
        }

        private static Rect2D Outset(Rect2D rect, float amount) => new(rect.X - amount, rect.Y - amount, rect.Width + (amount * 2), rect.Height + (amount * 2));
    }
}
