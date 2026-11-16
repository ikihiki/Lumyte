using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides two-dimensional geometry operations with explicit numeric and boundary policies.</summary>
public static partial class Geometry2D
{
    /// <summary>Evaluates a quadratic curve using De Casteljau interpolation.</summary>
    /// <typeparam name="T">The ordered coordinate arithmetic type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="amount">A finite parameter in the inclusive range zero to one.</param>
    /// <returns>The evaluated point.</returns>
    public static Point2D<T> EvaluateQuadraticBezier<T>(Point2D<T> start, Point2D<T> control, Point2D<T> end, T amount)
        where T : INumber<T>
    {
        ValidateAmount(amount);
        return Interpolate(Interpolate(start, control, amount), Interpolate(control, end, amount), amount);
    }

    /// <summary>Evaluates a cubic curve using De Casteljau interpolation.</summary>
    /// <typeparam name="T">The ordered coordinate arithmetic type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="amount">A finite parameter in the inclusive range zero to one.</param>
    /// <returns>The evaluated point.</returns>
    public static Point2D<T> EvaluateCubicBezier<T>(Point2D<T> start, Point2D<T> control1, Point2D<T> control2, Point2D<T> end, T amount)
        where T : INumber<T>
    {
        ValidateAmount(amount);
        return EvaluateQuadraticBezier(Interpolate(start, control1, amount), Interpolate(control1, control2, amount), Interpolate(control2, end, amount), amount);
    }

    /// <summary>Appends a quadratic polyline, including both endpoints, within explicit resource limits.</summary>
    /// <typeparam name="T">The ordered coordinate arithmetic type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The positive finite maximum control-point distance from each accepted segment.</param>
    /// <param name="destination">The list receiving the polyline; existing entries are preserved.</param>
    /// <param name="maxDepth">The maximum subdivision depth, between zero and 30.</param>
    /// <param name="maxPoints">The maximum number of appended points, at least two.</param>
    /// <returns>True if every segment met the tolerance; false if a budget was exhausted. Failure leaves the destination unchanged.</returns>
    /// <exception cref="OverflowException">A segment distance cannot be represented in the coordinate precision. The destination is unchanged.</exception>
    public static bool TryFlattenQuadraticBezier<T>(Point2D<T> start, Point2D<T> control, Point2D<T> end, T tolerance, List<Point2D<T>> destination, int maxDepth, int maxPoints)
        where T : INumber<T>
    {
        ValidateFlatten(tolerance, destination, maxDepth, maxPoints);
        var points = new List<Point2D<T>> { start };
        var pending = new Stack<(Point2D<T> Start, Point2D<T> Control, Point2D<T> End, int Depth)>();
        pending.Push((start, control, end, 0));
        while (pending.TryPop(out (Point2D<T> Start, Point2D<T> Control, Point2D<T> End, int Depth) curve))
        {
            if (WithinTolerance(curve.Start, curve.End, curve.Control, tolerance))
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

            T half = HalfParameter<T>();
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
    /// <typeparam name="T">The ordered coordinate arithmetic type.</typeparam>
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
    public static bool TryFlattenCubicBezier<T>(Point2D<T> start, Point2D<T> control1, Point2D<T> control2, Point2D<T> end, T tolerance, List<Point2D<T>> destination, int maxDepth, int maxPoints)
        where T : INumber<T>
    {
        ValidateFlatten(tolerance, destination, maxDepth, maxPoints);
        var points = new List<Point2D<T>> { start };
        var pending = new Stack<(Point2D<T> Start, Point2D<T> Control1, Point2D<T> Control2, Point2D<T> End, int Depth)>();
        pending.Push((start, control1, control2, end, 0));
        while (pending.TryPop(out (Point2D<T> Start, Point2D<T> Control1, Point2D<T> Control2, Point2D<T> End, int Depth) curve))
        {
            if (WithinTolerance(curve.Start, curve.End, curve.Control1, tolerance) && WithinTolerance(curve.Start, curve.End, curve.Control2, tolerance))
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

            T half = HalfParameter<T>();
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

    /// <summary>Evaluates a quadratic curve in an explicitly selected arithmetic type.</summary>
    /// <typeparam name="T">The input coordinate type.</typeparam>
    /// <typeparam name="TResult">The result and interpolation type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="amount">The finite parameter between zero and one.</param>
    /// <returns>The evaluated point.</returns>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public static Point2D<TResult> EvaluateQuadraticBezier<T, TResult>(Point2D<T> start, Point2D<T> control, Point2D<T> end, TResult amount)
        where T : INumber<T>
        where TResult : INumber<TResult> => EvaluateQuadraticBezier<TResult>(ConvertBezierPoint<T, TResult>(start), ConvertBezierPoint<T, TResult>(control), ConvertBezierPoint<T, TResult>(end), amount);

    /// <summary>Flattens a quadratic curve using the selected result arithmetic.</summary>
    /// <typeparam name="T">The input coordinate type.</typeparam>
    /// <typeparam name="TResult">The result and subdivision type; must represent one half.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The positive finite tolerance.</param>
    /// <param name="destination">The output polyline; unchanged on budget failure.</param>
    /// <param name="maxDepth">The subdivision limit, zero to 30.</param>
    /// <param name="maxPoints">The appended point limit, at least two.</param>
    /// <returns>Whether flattening completed within the budgets.</returns>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public static bool TryFlattenQuadraticBezier<T, TResult>(Point2D<T> start, Point2D<T> control, Point2D<T> end, TResult tolerance, List<Point2D<TResult>> destination, int maxDepth, int maxPoints)
        where T : INumber<T>
        where TResult : INumber<TResult> => TryFlattenQuadraticBezier<TResult>(ConvertBezierPoint<T, TResult>(start), ConvertBezierPoint<T, TResult>(control), ConvertBezierPoint<T, TResult>(end), tolerance, destination, maxDepth, maxPoints);

    /// <summary>Evaluates a cubic curve in an explicitly selected arithmetic type.</summary>
    /// <typeparam name="T">The input coordinate type.</typeparam>
    /// <typeparam name="TResult">The result and interpolation type.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The control1 point.</param>
    /// <param name="control2">The control2 point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="amount">The finite parameter between zero and one.</param>
    /// <returns>The evaluated point.</returns>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public static Point2D<TResult> EvaluateCubicBezier<T, TResult>(Point2D<T> start, Point2D<T> control1, Point2D<T> control2, Point2D<T> end, TResult amount)
        where T : INumber<T>
        where TResult : INumber<TResult> => EvaluateCubicBezier<TResult>(ConvertBezierPoint<T, TResult>(start), ConvertBezierPoint<T, TResult>(control1), ConvertBezierPoint<T, TResult>(control2), ConvertBezierPoint<T, TResult>(end), amount);

    /// <summary>Flattens a cubic curve using the selected result arithmetic.</summary>
    /// <typeparam name="T">The input coordinate type.</typeparam>
    /// <typeparam name="TResult">The result and subdivision type; must represent one half.</typeparam>
    /// <param name="start">The start point.</param>
    /// <param name="control1">The control1 point.</param>
    /// <param name="control2">The control2 point.</param>
    /// <param name="end">The end point.</param>
    /// <param name="tolerance">The positive finite tolerance.</param>
    /// <param name="destination">The output polyline; unchanged on budget failure.</param>
    /// <param name="maxDepth">The subdivision limit, zero to 30.</param>
    /// <param name="maxPoints">The appended point limit, at least two.</param>
    /// <returns>Whether flattening completed within the budgets.</returns>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public static bool TryFlattenCubicBezier<T, TResult>(Point2D<T> start, Point2D<T> control1, Point2D<T> control2, Point2D<T> end, TResult tolerance, List<Point2D<TResult>> destination, int maxDepth, int maxPoints)
        where T : INumber<T>
        where TResult : INumber<TResult> => TryFlattenCubicBezier<TResult>(ConvertBezierPoint<T, TResult>(start), ConvertBezierPoint<T, TResult>(control1), ConvertBezierPoint<T, TResult>(control2), ConvertBezierPoint<T, TResult>(end), tolerance, destination, maxDepth, maxPoints);

    private static Point2D<TResult> ConvertBezierPoint<T, TResult>(Point2D<T> point)
        where T : INumber<T>
        where TResult : INumber<TResult> => new(TResult.CreateChecked(point.X), TResult.CreateChecked(point.Y));

    private static T HalfParameter<T>()
        where T : INumber<T>
    {
        T half = T.One / (T.One + T.One);
        if (half == T.Zero || half + half != T.One)
        {
            throw new ArithmeticException("Subdivision requires a type representing one half. Use TryFlattenQuadraticBezier<T, TResult> or TryFlattenCubicBezier<T, TResult> with a fractional result type.");
        }

        return half;
    }

    private static Point2D<T> Interpolate<T>(Point2D<T> start, Point2D<T> end, T amount)
        where T : INumber<T>
    {
        if (amount == T.Zero)
        {
            return start;
        }

        if (amount == T.One)
        {
            return end;
        }

        if (typeof(T) == typeof(decimal))
        {
            var parameter = GeometryRational.From(amount);
            GeometryRational inverse = GeometryRational.From(T.One) - parameter;
            return new(
                ((GeometryRational.From(start.X) * inverse) + (GeometryRational.From(end.X) * parameter)).To<T>(),
                ((GeometryRational.From(start.Y) * inverse) + (GeometryRational.From(end.Y) * parameter)).To<T>());
        }

        T complement = T.One - amount;
        return new Point2D<T>(InterpolateCoordinate(start.X, end.X, amount, complement), InterpolateCoordinate(start.Y, end.Y, amount, complement));
    }

    private static T InterpolateCoordinate<T>(T start, T end, T amount, T complement)
        where T : INumber<T>
    {
        if (start == end && start != T.Zero)
        {
            return start;
        }

        T first = checked(start * complement);
        T second = checked(end * amount);
        if (GeometryScalar<T>.IsIeee
            && (T.IsSubnormal(first) || T.IsSubnormal(second)
                || (first == T.Zero && start != T.Zero) || (second == T.Zero && end != T.Zero))
            && T.IsFinite(start) && T.IsFinite(end))
        {
            // Subnormal products can round before their sum, losing a representable
            // coordinate. Round the exact convex combination once in this rare case.
            // Do not use end - start: opposite finite extremes can overflow there.
            var parameter = GeometryRational.From(amount);
            GeometryRational inverse = GeometryRational.From(T.One) - parameter;
            return ((GeometryRational.From(start) * inverse) + (GeometryRational.From(end) * parameter)).To<T>();
        }

        return checked(first + second);
    }

    private static bool WithinTolerance<T>(Point2D<T> start, Point2D<T> end, Point2D<T> control, T tolerance)
        where T : INumber<T>
    {
        if (GeometryScalar<T>.IsIeee || !GeometryRational.Supports<T>())
        {
            // Retain the established IEEE projection, root and comparison path.
            return new Segment2D<T>(start, end).DistanceTo(control) <= tolerance;
        }

        GeometryRational dx = GeometryRational.From(end.X) - GeometryRational.From(start.X);
        GeometryRational dy = GeometryRational.From(end.Y) - GeometryRational.From(start.Y);
        GeometryRational px = GeometryRational.From(control.X) - GeometryRational.From(start.X);
        GeometryRational py = GeometryRational.From(control.Y) - GeometryRational.From(start.Y);
        GeometryRational lengthSquared = (dx * dx) + (dy * dy);
        GeometryRational projection = (dx * px) + (dy * py);
        GeometryRational distanceSquared;
        if (lengthSquared.Numerator.IsZero || projection.Numerator.Sign <= 0)
        {
            distanceSquared = (px * px) + (py * py);
        }
        else if ((projection - lengthSquared).Numerator.Sign >= 0)
        {
            GeometryRational ex = px - dx;
            GeometryRational ey = py - dy;
            distanceSquared = (ex * ex) + (ey * ey);
        }
        else
        {
            GeometryRational cross = (dx * py) - (dy * px);
            distanceSquared = (cross * cross) / lengthSquared;
        }

        var limit = GeometryRational.From(tolerance);
        return (distanceSquared - (limit * limit)).Numerator.Sign <= 0;
    }

    private static void ValidateAmount<T>(T amount)
        where T : INumber<T>
    {
        if (!T.IsFinite(amount) || amount < T.Zero || amount > T.One)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }
    }

    private static void ValidateFlatten<T>(T tolerance, List<Point2D<T>> destination, int maxDepth, int maxPoints)
        where T : INumber<T>
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
