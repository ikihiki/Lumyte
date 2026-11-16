using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>
/// Immutable raw-curve input and fixed GPU output reservations. This is not a flattened path.
/// Coordinates remain logical; the GPU applies Transform. All offsets count elements, not bytes.
/// The caller must aggregate Capacity across draws before allocating, and charge paint/clip and
/// other owned resources through ComputeGeometryCapacity2D.Combine and Validate.
/// </summary>
internal sealed class ComputeGeometryPlan2D
{
    private ComputeGeometryPlan2D(
        ComputeCurveInput2D[] curves,
        Matrix3x2 transform,
        FillRule2D fillRule,
        StrokeStyle2D? stroke,
        ComputeGeometryCapacity2D capacity,
        ComputeTileBounds2D bounds,
        int contours,
        int roundDivisions)
    {
        Curves = Array.AsReadOnly(curves);
        Transform = transform;
        FillRule = fillRule;
        Stroke = stroke;
        Capacity = capacity;
        TileBounds = bounds;
        ContourCount = contours;
        RoundDivisions = roundDivisions;
    }

    internal IReadOnlyList<ComputeCurveInput2D> Curves { get; }

    internal Matrix3x2 Transform { get; }

    internal FillRule2D FillRule { get; }

    internal StrokeStyle2D? Stroke { get; }

    internal ComputeGeometryCapacity2D Capacity { get; }

    internal ComputeTileBounds2D TileBounds { get; }

    internal int ContourCount { get; }

    internal int RoundDivisions { get; }

    /// <summary>
    /// Encodes a single path, including explicit and fill-only implicit closing edges.
    /// Transform must already contain DPR and pixel origin. No inverse is required.
    /// Tile coordinates are signed device-space 16-pixel tiles, not clipped to the target:
    /// offscreen edges must remain available for backdrop propagation. Device hulls are bounded
    /// to +/- 2^20 pixels; shader evaluation must transform controls before de Casteljau and
    /// enforce the one-pixel hull guard (large cancelling logical transforms are rejected).
    /// </summary>
    internal static ComputeGeometryPlan2D Create(
        Path2D path,
        Matrix3x2 transform,
        FillRule2D fillRule = FillRule2D.NonZero,
        StrokeStyle2D? stroke = null,
        long maxWorkingBytes = 256L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (maxWorkingBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWorkingBytes));
        }

        if (!Enum.IsDefined(fillRule))
        {
            throw new ArgumentOutOfRangeException(nameof(fillRule));
        }

        ValidateTransform(transform);
        if (stroke is not null && (!float.IsFinite(stroke.Width) || stroke.Width <= 0 ||
            !float.IsFinite(stroke.MiterLimit) || stroke.MiterLimit < 1 ||
            !Enum.IsDefined(stroke.Cap) || !Enum.IsDefined(stroke.Join)))
        {
            throw new ArgumentException("Invalid stroke style.", nameof(stroke));
        }

        try
        {
            return Build(path, transform, fillRule, stroke, maxWorkingBytes);
        }
        catch (OverflowException exception)
        {
            throw new NotSupportedException("Compute geometry capacity overflow.", exception);
        }
    }

    private static ComputeGeometryPlan2D Build(
        Path2D path,
        Matrix3x2 transform,
        FillRule2D fillRule,
        StrokeStyle2D? stroke,
        long maxWorkingBytes)
    {
        // Two passes: validate and budget before allocating even the raw input array.
        int count = 0;
        int edges = 0;
        int contours = 0;
        long references = 0;
        Bounds hull = default;
        Visit(null);
        int divisions = RoundCount(stroke, transform);
        long triangles = stroke is null ? 0 : checked((2L * edges) +
            ((edges + (4L * contours)) * Math.Max(2, divisions)));
        if (triangles > 65536)
        {
            throw new NotSupportedException("Stroke exceeds the 65536 triangle reservation budget.");
        }

        if (stroke is not null && hull.HasValue)
        {
            // Frobenius norm bounds every transformed logical offset. sqrt(2) covers
            // square-cap corners, including an isolated square; miter covers joins.
            double radius = stroke.Width * 0.5 * Scale(transform) *
                Math.Max(Math.Sqrt(2), stroke.Join == LineJoin2D.Miter ? stroke.MiterLimit : 1);
            hull = hull.Expand(radius);
        }

        ComputeTileBounds2D tiles = hull.Tiles();
        long tileCount = tiles.Count;
        if (stroke is not null)
        {
            references = checked(triangles * 3 * tileCount);
        }

        long backdrop = checked((tiles.Width + 1L) * (tiles.Height + 1L));
        if (!hull.HasValue)
        {
            backdrop = 0;
        }

        // Scan has a count and exclusive-prefix slot for each producer and reference,
        // plus a second full-size scratch level: sufficient for hierarchical scan.
        long generatedEdges = stroke is null ? edges : checked(triangles * 3);
        long scan = checked(2 * (count + edges + generatedEdges + triangles + references + tileCount + backdrop + 1));
        long vectors = checked((count * 6L) + (stroke is null ? edges : triangles * 3));
        long bytes = checked(1024 + (count * 96L * 3) + ((edges + generatedEdges) * 32L) +
            (triangles * 48) + (references * 16) + (tileCount * 16) +
            (backdrop * 8) + (scan * 8));
        var capacity = new ComputeGeometryCapacity2D(
            edges,
            triangles,
            references,
            tileCount,
            backdrop,
            scan,
            vectors,
            bytes);
        capacity.Validate(maxWorkingBytes);
        var encoded = new ComputeCurveInput2D[count];
        Visit(encoded);
        return new(encoded, transform, fillRule, stroke, capacity, tiles, contours, divisions);

        void Visit(ComputeCurveInput2D[]? output)
        {
            Vector2 position = default;
            Vector2 start = default;
            bool active = false;
            bool closed = false;
            int contour = -1;
            int index = 0;
            int edgeOffset = 0;
            foreach (PathSegment2D segment in path.Segments)
            {
                if (segment.Verb == PathVerb2D.Move)
                {
                    Finish();
                    position = start = segment.P0;
                    Point(position);
                    active = true;
                    closed = false;
                    contour++;
                    if (output is null)
                    {
                        contours++;
                    }

                    // Move is retained: isolated stroke points also need GPU input.
                    Emit(PathVerb2D.Move, position, position, position, position, 0, false);
                    continue;
                }

                if (!active)
                {
                    throw new NotSupportedException("A curve requires an initial Move.");
                }

                if (segment.Verb == PathVerb2D.Close)
                {
                    if (!closed)
                    {
                        Emit(PathVerb2D.Close, position, start, start, start, 1, false);
                        position = start;
                        closed = true;
                    }

                    continue;
                }

                if (closed)
                {
                    // PathFlattening2D starts a fresh contour at the closed start.
                    contour++;
                    if (output is null)
                    {
                        contours++;
                    }

                    start = position;
                    closed = false;
                    Emit(PathVerb2D.Move, position, position, position, position, 0, false);
                }

                Vector2 a = position;
                switch (segment.Verb)
                {
                    case PathVerb2D.Line:
                        Emit(segment.Verb, a, segment.P0, segment.P0, segment.P0, 1, false);
                        position = segment.P0;
                        break;
                    case PathVerb2D.Quadratic:
                        Emit(
                            segment.Verb,
                            a,
                            segment.P0,
                            segment.P1,
                            segment.P1,
                            Subdivisions(a, segment.P0, segment.P1, segment.P1, false, transform, stroke),
                            false);
                        position = segment.P1;
                        break;
                    case PathVerb2D.Cubic:
                        Emit(
                            segment.Verb,
                            a,
                            segment.P0,
                            segment.P1,
                            segment.P2,
                            Subdivisions(a, segment.P0, segment.P1, segment.P2, true, transform, stroke),
                            false);
                        position = segment.P2;
                        break;
                    default:
                        throw new NotSupportedException("Unsupported curve verb.");
                }
            }

            Finish();

            void Finish()
            {
                if (active && !closed && stroke is null)
                {
                    Emit(PathVerb2D.Close, position, start, start, start, 1, true);
                }
            }

            void Emit(PathVerb2D verb, Vector2 a, Vector2 b, Vector2 c, Vector2 d, int n, bool implicitClose)
            {
                if (output is not null)
                {
                    output[index++] = new(verb, a, b, c, d, contour, edgeOffset, n, implicitClose);
                    edgeOffset += n;
                    return;
                }

                Bounds local = default;
                local = local.Include(Device(a, transform)).Include(Device(b, transform))
                    .Include(Device(c, transform)).Include(Device(d, transform));
                hull = hull.Union(local);
                count = checked(count + 1);
                if (count * 6L > 262144)
                {
                    throw new NotSupportedException("Raw curve inputs exceed the geometry vector budget.");
                }

                edges = checked(edges + n);
                if (edges > 65536)
                {
                    throw new NotSupportedException("Path exceeds the 65536 edge reservation budget.");
                }

                references = checked(references + (local.Tiles().Count * n));
            }
        }
    }

    private static int Subdivisions(Vector2 a, Vector2 b, Vector2 c, Vector2 d, bool cubic, Matrix3x2 transform, StrokeStyle2D? stroke)
    {
        if (stroke is not null)
        {
            return ComputeStrokeSubdivision2D.Count(a, b, c, d, cubic, transform, stroke.Width);
        }

        // Transform second differences directly: translation cancellation cannot destroy curvature.
        double x = (double)a.X - (2.0 * b.X) + c.X;
        double y = (double)a.Y - (2.0 * b.Y) + c.Y;
        double m = Norm((x * transform.M11) + (y * transform.M21), (x * transform.M12) + (y * transform.M22));
        if (cubic)
        {
            x = (double)b.X - (2.0 * c.X) + d.X;
            y = (double)b.Y - (2.0 * c.Y) + d.Y;
            m = Math.Max(m, Norm((x * transform.M11) + (y * transform.M21), (x * transform.M12) + (y * transform.M22)));
        }

        m *= cubic ? 6 : 2;
        double n = Math.Max(1, Math.Ceiling(Math.Sqrt(m / (8 * 0.125))));

        // Verify the inequality too: sqrt can round down at an integer boundary.
        if (m / (8 * n * n) > 0.125)
        {
            n++;
        }

        if (!double.IsFinite(n) || n > 65536)
        {
            throw new NotSupportedException("Curve exceeds the subdivision budget.");
        }

        return (int)n;
    }

    private static int RoundCount(StrokeStyle2D? stroke, Matrix3x2 transform)
    {
        if (stroke is null || (stroke.Cap != LineCap2D.Round && stroke.Join != LineJoin2D.Round))
        {
            return 0;
        }

        double required = Math.Ceiling(Math.PI / Math.Acos(Math.Clamp(
            1 - (0.125 / Math.Max(stroke.Width * 0.5 * Scale(transform), 0.125)),
            -1,
            1)));
        if (!double.IsFinite(required) || required > 4096)
        {
            throw new NotSupportedException("Round stroke exceeds the subdivision budget.");
        }

        return Math.Max(8, (int)required);
    }

    private static double Scale(Matrix3x2 m) => Math.Sqrt(((double)m.M11 * m.M11) +
        ((double)m.M12 * m.M12) + ((double)m.M21 * m.M21) + ((double)m.M22 * m.M22));

    private static double Norm(double x, double y) => Math.Sqrt((x * x) + (y * y));

    private static void ValidateTransform(Matrix3x2 m)
    {
        if (!float.IsFinite(m.M11) || !float.IsFinite(m.M12) || !float.IsFinite(m.M21) ||
            !float.IsFinite(m.M22) || !float.IsFinite(m.M31) || !float.IsFinite(m.M32))
        {
            throw new ArgumentException("Transform must be finite.", nameof(m));
        }
    }

    private static void Point(Vector2 p)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y))
        {
            throw new NotSupportedException("Curve points must be finite.");
        }
    }

    private static (double X, double Y) Device(Vector2 p, Matrix3x2 m)
    {
        Point(p);
        double magnitude = Math.Max(
            Math.Abs((double)p.X * m.M11) + Math.Abs((double)p.Y * m.M21) + Math.Abs(m.M31),
            Math.Abs((double)p.X * m.M12) + Math.Abs((double)p.Y * m.M22) + Math.Abs(m.M32));
        if (magnitude > 1048576)
        {
            throw new NotSupportedException("Transform intermediate magnitude exceeds the float32 hull guard.");
        }

        double x = ((double)p.X * m.M11) + ((double)p.Y * m.M21) + m.M31;
        double y = ((double)p.X * m.M12) + ((double)p.Y * m.M22) + m.M32;
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue)
        {
            throw new NotSupportedException("Device control hull is not finite float32.");
        }

        return (x, y);
    }

    private readonly record struct Bounds(bool HasValue, double Left, double Top, double Right, double Bottom)
    {
        internal Bounds Include((double X, double Y) p) => !HasValue ? new(true, p.X, p.Y, p.X, p.Y) :
            new(true, Math.Min(Left, p.X), Math.Min(Top, p.Y), Math.Max(Right, p.X), Math.Max(Bottom, p.Y));

        internal Bounds Union(Bounds b) => !b.HasValue ? this : Include((b.Left, b.Top)).Include((b.Right, b.Bottom));

        internal Bounds Expand(double r) => new(true, Left - r, Top - r, Right + r, Bottom + r);

        internal ComputeTileBounds2D Tiles()
        {
            if (!HasValue)
            {
                return default;
            }

            // Include the far boundary tile, even for a zero-area hull. One device-pixel
            // guard bounds float32 evaluation rounding in the supported coordinate range.
            double l = Math.Floor((Left - 1) / 16);
            double t = Math.Floor((Top - 1) / 16);
            double r = Math.Floor((Right + 1) / 16) + 1;
            double b = Math.Floor((Bottom + 1) / 16) + 1;
            if (l < int.MinValue || t < int.MinValue || r > int.MaxValue || b > int.MaxValue ||
                Math.Max(Math.Max(Math.Abs(Left), Math.Abs(Right)), Math.Max(Math.Abs(Top), Math.Abs(Bottom))) > 1048576)
            {
                throw new NotSupportedException("Control hull exceeds the supported device-coordinate range.");
            }

            return new((int)l, (int)t, checked((int)(r - l)), checked((int)(b - t)));
        }
    }
}
