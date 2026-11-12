using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Evaluates and adaptively flattens planar Bezier curves without narrowing coordinates.</summary>
public static class Bezier2D
{
    /// <summary>Evaluates a quadratic curve using De Casteljau interpolation.</summary>
    /// <typeparam name="T">The floating-point coordinate type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="amount">A finite parameter in the inclusive range zero to one.</param>
    /// <returns>The evaluated point.</returns>
    public static Point2D<T> Quadratic<T>(Point2D<T> start, Point2D<T> control, Point2D<T> end, T amount)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ValidateAmount(amount);
        return Interpolate(Interpolate(start, control, amount), Interpolate(control, end, amount), amount);
    }

    /// <summary>Evaluates a cubic curve using De Casteljau interpolation.</summary>
    /// <typeparam name="T">The floating-point coordinate type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="amount">A finite parameter in the inclusive range zero to one.</param>
    /// <returns>The evaluated point.</returns>
    public static Point2D<T> Cubic<T>(Point2D<T> start, Point2D<T> control1, Point2D<T> control2, Point2D<T> end, T amount)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ValidateAmount(amount);
        return Quadratic(Interpolate(start, control1, amount), Interpolate(control1, control2, amount), Interpolate(control2, end, amount), amount);
    }

    /// <summary>Appends a quadratic polyline, including both endpoints, within explicit resource limits.</summary>
    /// <typeparam name="T">The floating-point coordinate type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The positive finite maximum control-point distance from each accepted segment.</param>
    /// <param name="destination">The list receiving the polyline; existing entries are preserved.</param>
    /// <param name="maxDepth">The maximum subdivision depth, between zero and 30.</param>
    /// <param name="maxPoints">The maximum number of appended points, at least two.</param>
    /// <returns>True if every segment met the tolerance; false if a budget was exhausted. Failure leaves the destination unchanged.</returns>
    /// <exception cref="OverflowException">A segment distance cannot be represented in the coordinate precision. The destination is unchanged.</exception>
    public static bool TryFlattenQuadratic<T>(Point2D<T> start, Point2D<T> control, Point2D<T> end, T tolerance, List<Point2D<T>> destination, int maxDepth, int maxPoints)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ValidateFlatten(tolerance, destination, maxDepth, maxPoints);
        var points = new List<Point2D<T>> { start };
        var pending = new Stack<(Point2D<T> Start, Point2D<T> Control, Point2D<T> End, int Depth)>();
        pending.Push((start, control, end, 0));
        T half = T.One / (T.One + T.One);
        while (pending.TryPop(out (Point2D<T> Start, Point2D<T> Control, Point2D<T> End, int Depth) curve))
        {
            if (new Segment2D<T>(curve.Start, curve.End).DistanceTo(curve.Control) <= tolerance)
            {
                if (points.Count >= maxPoints)
                {
                    return false;
                }

                points.Add(curve.End);
                continue;
            }

            if (curve.Depth >= maxDepth || points.Count + pending.Count + 2 > maxPoints)
            {
                return false;
            }

            Point2D<T> a = Interpolate(curve.Start, curve.Control, half);
            Point2D<T> b = Interpolate(curve.Control, curve.End, half);
            Point2D<T> middle = Interpolate(a, b, half);
            pending.Push((middle, b, curve.End, curve.Depth + 1));
            pending.Push((curve.Start, a, middle, curve.Depth + 1));
        }

        destination.AddRange(points);
        return true;
    }

    /// <summary>Appends a cubic polyline, including both endpoints, within explicit resource limits.</summary>
    /// <typeparam name="T">The floating-point coordinate type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The positive finite maximum control-point distance from each accepted segment.</param>
    /// <param name="destination">The list receiving the polyline; existing entries are preserved.</param>
    /// <param name="maxDepth">The maximum subdivision depth, between zero and 30.</param>
    /// <param name="maxPoints">The maximum number of appended points, at least two.</param>
    /// <returns>True if every segment met the tolerance; false if a budget was exhausted. Failure leaves the destination unchanged.</returns>
    /// <exception cref="OverflowException">A segment distance cannot be represented in the coordinate precision. The destination is unchanged.</exception>
    public static bool TryFlattenCubic<T>(Point2D<T> start, Point2D<T> control1, Point2D<T> control2, Point2D<T> end, T tolerance, List<Point2D<T>> destination, int maxDepth, int maxPoints)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ValidateFlatten(tolerance, destination, maxDepth, maxPoints);
        var points = new List<Point2D<T>> { start };
        var pending = new Stack<(Point2D<T> Start, Point2D<T> Control1, Point2D<T> Control2, Point2D<T> End, int Depth)>();
        pending.Push((start, control1, control2, end, 0));
        T half = T.One / (T.One + T.One);
        while (pending.TryPop(out (Point2D<T> Start, Point2D<T> Control1, Point2D<T> Control2, Point2D<T> End, int Depth) curve))
        {
            var chord = new Segment2D<T>(curve.Start, curve.End);
            if (chord.DistanceTo(curve.Control1) <= tolerance && chord.DistanceTo(curve.Control2) <= tolerance)
            {
                if (points.Count >= maxPoints)
                {
                    return false;
                }

                points.Add(curve.End);
                continue;
            }

            if (curve.Depth >= maxDepth || points.Count + pending.Count + 2 > maxPoints)
            {
                return false;
            }

            Point2D<T> a = Interpolate(curve.Start, curve.Control1, half);
            Point2D<T> b = Interpolate(curve.Control1, curve.Control2, half);
            Point2D<T> c = Interpolate(curve.Control2, curve.End, half);
            Point2D<T> d = Interpolate(a, b, half);
            Point2D<T> e = Interpolate(b, c, half);
            Point2D<T> middle = Interpolate(d, e, half);
            pending.Push((middle, e, c, curve.End, curve.Depth + 1));
            pending.Push((curve.Start, a, d, middle, curve.Depth + 1));
        }

        destination.AddRange(points);
        return true;
    }

    private static Point2D<T> Interpolate<T>(Point2D<T> start, Point2D<T> end, T amount)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (amount == T.Zero)
        {
            return start;
        }

        if (amount == T.One)
        {
            return end;
        }

        T complement = T.One - amount;
        return new Point2D<T>((start.X * complement) + (end.X * amount), (start.Y * complement) + (end.Y * amount));
    }

    private static void ValidateAmount<T>(T amount)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (!T.IsFinite(amount) || amount < T.Zero || amount > T.One)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
    }

    private static void ValidateFlatten<T>(T tolerance, List<Point2D<T>> destination, int maxDepth, int maxPoints)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!T.IsFinite(tolerance) || tolerance <= T.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDepth, 30);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoints, 2);
    }
}
