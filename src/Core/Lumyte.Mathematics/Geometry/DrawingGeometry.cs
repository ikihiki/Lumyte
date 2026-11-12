using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides streaming single-precision geometry with BCL vector arithmetic.</summary>
/// <remarks>These operations retain single-precision evaluation order rather than the validated generic geometry contract.</remarks>
public static class DrawingGeometry
{
    /// <summary>Receives flattened endpoints without requiring a delegate or boxing.</summary>
    public interface IPointSink
    {
        /// <summary>Appends the next accepted endpoint.</summary>
        /// <param name="point">The next accepted point.</param>
        void Append(Vector2 point);
    }

    /// <summary>Computes the distance to a closed segment using BCL single-precision projection.</summary>
    /// <param name="point">The query point.</param>
    /// <param name="start">The segment start.</param>
    /// <param name="end">The segment end.</param>
    /// <returns>The distance; non-finite arithmetic follows BCL semantics.</returns>
    public static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
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

    /// <summary>Includes a point in an incremental BCL bounds reduction without allocating.</summary>
    /// <param name="point">The next point.</param>
    /// <param name="minimum">The minimum, initially positive infinity.</param>
    /// <param name="maximum">The maximum, initially negative infinity.</param>
    public static void IncludeInBounds(Vector2 point, ref Vector2 minimum, ref Vector2 maximum)
    {
        minimum = Vector2.Min(minimum, point);
        maximum = Vector2.Max(maximum, point);
    }

    /// <summary>Appends quadratic endpoints through a callback.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The acceptance distance.</param>
    /// <param name="append">Receives each accepted endpoint.</param>
    /// <param name="maxDepth">The maximum subdivision depth, from zero through 30.</param>
    public static void AppendFlattenedQuadratic(Vector2 start, Vector2 control, Vector2 end, float tolerance, Action<Vector2> append, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(append);
        var sink = new ActionSink(append);
        AppendFlattenedQuadratic(start, control, end, tolerance, ref sink, maxDepth);
    }

    /// <summary>Appends cubic endpoints through a callback.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The acceptance distance.</param>
    /// <param name="append">Receives each accepted endpoint.</param>
    /// <param name="maxDepth">The maximum subdivision depth, from zero through 30.</param>
    public static void AppendFlattenedCubic(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, float tolerance, Action<Vector2> append, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(append);
        var sink = new ActionSink(append);
        AppendFlattenedCubic(start, control1, control2, end, tolerance, ref sink, maxDepth);
    }

    /// <summary>Appends quadratic endpoints in order using a non-boxed mutable sink.</summary>
    /// <remarks>Earlier endpoints remain appended when subdivision or the sink throws. No finite validation is performed.</remarks>
    /// <typeparam name="TSink">The non-boxed endpoint sink.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The acceptance distance.</param>
    /// <param name="sink">Receives endpoints and retains mutable state.</param>
    /// <param name="maxDepth">The maximum subdivision depth, from zero through 30.</param>
    public static void AppendFlattenedQuadratic<TSink>(Vector2 start, Vector2 control, Vector2 end, float tolerance, ref TSink sink, int maxDepth)
        where TSink : struct, IPointSink
    {
        ValidateDepth(maxDepth);
        Quadratic(start, control, end, tolerance, ref sink, maxDepth, 0);
    }

    /// <summary>Appends cubic endpoints in order using a non-boxed mutable sink.</summary>
    /// <remarks>Earlier endpoints remain appended when subdivision or the sink throws. No finite validation is performed.</remarks>
    /// <typeparam name="TSink">The non-boxed endpoint sink.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The acceptance distance.</param>
    /// <param name="sink">Receives endpoints and retains mutable state.</param>
    /// <param name="maxDepth">The maximum subdivision depth, from zero through 30.</param>
    public static void AppendFlattenedCubic<TSink>(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, float tolerance, ref TSink sink, int maxDepth)
        where TSink : struct, IPointSink
    {
        ValidateDepth(maxDepth);
        Cubic(start, control1, control2, end, tolerance, ref sink, maxDepth, 0);
    }

    private static void Quadratic<TSink>(Vector2 p0, Vector2 p1, Vector2 p2, float tolerance, ref TSink sink, int maxDepth, int depth)
        where TSink : struct, IPointSink
    {
        if (DistanceToSegment(p1, p0, p2) <= tolerance)
        {
            sink.Append(p2);
            return;
        }

        if (depth >= maxDepth)
        {
            throw new NotSupportedException("Curve subdivision exceeds the precision budget.");
        }

        Vector2 a = (p0 + p1) * 0.5f;
        Vector2 b = (p1 + p2) * 0.5f;
        Vector2 midpoint = (a + b) * 0.5f;
        Quadratic(p0, a, midpoint, tolerance, ref sink, maxDepth, depth + 1);
        Quadratic(midpoint, b, p2, tolerance, ref sink, maxDepth, depth + 1);
    }

    private static void Cubic<TSink>(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tolerance, ref TSink sink, int maxDepth, int depth)
        where TSink : struct, IPointSink
    {
        if (MathF.Max(DistanceToSegment(p1, p0, p3), DistanceToSegment(p2, p0, p3)) <= tolerance)
        {
            sink.Append(p3);
            return;
        }

        if (depth >= maxDepth)
        {
            throw new NotSupportedException("Curve subdivision exceeds the precision budget.");
        }

        Vector2 a = (p0 + p1) * 0.5f;
        Vector2 b = (p1 + p2) * 0.5f;
        Vector2 c = (p2 + p3) * 0.5f;
        Vector2 d = (a + b) * 0.5f;
        Vector2 e = (b + c) * 0.5f;
        Vector2 f = (d + e) * 0.5f;
        Cubic(p0, a, d, f, tolerance, ref sink, maxDepth, depth + 1);
        Cubic(f, e, c, p3, tolerance, ref sink, maxDepth, depth + 1);
    }

    private static void ValidateDepth(int maxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDepth, 30);
    }

    private readonly struct ActionSink(Action<Vector2> append) : IPointSink
    {
        public void Append(Vector2 point) => append(point);
    }
}
