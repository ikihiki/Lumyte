using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed 2-dimensional segment, including coincident endpoints.</summary>
/// <typeparam name="T">The ordered numeric coordinate type.</typeparam>
public readonly record struct Segment2D<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="Segment2D{T}"/> struct.</summary>
    /// <param name="start">The first endpoint.</param>
    /// <param name="end">The second endpoint.</param>
    public Segment2D(Point2D<T> start, Point2D<T> end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Gets the first endpoint.</summary>
    public Point2D<T> Start { get; }

    /// <summary>Gets the second endpoint.</summary>
    public Point2D<T> End { get; }

    /// <summary>Gets the Euclidean endpoint distance.</summary>
    public T Length => Start.DistanceTo(End);

    /// <summary>Finds the closest point using a scaled, endpoint-clamped projection.</summary>
    /// <param name="point">The point to project.</param>
    /// <returns>The closest point, or the endpoint for a zero-length segment.</returns>
    /// <exception cref="OverflowException">A required difference or result is not finite.</exception>
    public Point2D<T> ClosestPoint(Point2D<T> point)
    {
        if (!GeometryScalar<T>.IsIeee)
        {
            return ClosestPoint<T>(point);
        }

        if (Start == End)
        {
            return Start;
        }

        T dx = GeometryScalar<T>.Checked(checked(End.X - Start.X));
        T dy = GeometryScalar<T>.Checked(checked(End.Y - Start.Y));
        T px = GeometryScalar<T>.Checked(checked(point.X - Start.X));
        T py = GeometryScalar<T>.Checked(checked(point.Y - Start.Y));
        (T Numerator, T Denominator, int Exponent) amount = GeometryScalar<T>.Projection(dx, dy, T.Zero, px, py, T.Zero);
        return new Point2D<T>(GeometryScalar<T>.Lerp(Start.X, End.X, amount), GeometryScalar<T>.Lerp(Start.Y, End.Y, amount));
    }

    /// <summary>Returns the distance to the closest point on this closed segment.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>The Euclidean distance.</returns>
    /// <exception cref="OverflowException">A required difference or result is not finite.</exception>
    public T DistanceTo(Point2D<T> point) => GeometryScalar<T>.IsIeee ? point.DistanceTo(ClosestPoint(point)) : DistanceTo<T>(point);

    /// <summary>Tests closed intersection, including collinear overlap and endpoint contact.</summary>
    /// <param name="other">The other segment.</param>
    /// <returns>Whether the segments intersect in the selected floating-point precision.</returns>
    public bool Intersects(Segment2D<T> other)
    {
        int a = GeometryScalar<T>.OrientationSign(Start, End, other.Start);
        int b = GeometryScalar<T>.OrientationSign(Start, End, other.End);
        int c = GeometryScalar<T>.OrientationSign(other.Start, other.End, Start);
        int d = GeometryScalar<T>.OrientationSign(other.Start, other.End, End);
        return (a == 0 && OnSegment(other.Start))
            || (b == 0 && OnSegment(other.End))
            || (c == 0 && other.OnSegment(Start))
            || (d == 0 && other.OnSegment(End))
            || (((a < 0 && b > 0) || (a > 0 && b < 0))
                && ((c < 0 && d > 0) || (c > 0 && d < 0)));
    }

    /// <summary>Projects into an explicit numeric type. Integral coordinates must be exact.</summary>
    /// <typeparam name="TResult">The result coordinate type.</typeparam>
    /// <param name="point">The query point.</param>
    /// <returns>The closest point.</returns>
    public Point2D<TResult> ClosestPoint<TResult>(Point2D<T> point)
        where TResult : INumber<TResult>
    {
        if (GeometryRational.Supports<T>() || typeof(T) != typeof(TResult))
        {
            (GeometryRational X, GeometryRational Y) closest = ExactClosestPoint(point);
            return new(closest.X.To<TResult>(), closest.Y.To<TResult>());
        }

        var segment = new Segment2D<TResult>(Start.ConvertChecked<TResult>(), End.ConvertChecked<TResult>());
        if (GeometryScalar<TResult>.IsIeee)
        {
            return segment.ClosestPoint(point.ConvertChecked<TResult>());
        }

        if (segment.Start == segment.End)
        {
            return segment.Start;
        }

        TResult dX = checked(segment.End.X - segment.Start.X);
        TResult pX = checked(TResult.CreateChecked(point.X) - segment.Start.X);
        TResult dY = checked(segment.End.Y - segment.Start.Y);
        TResult pY = checked(TResult.CreateChecked(point.Y) - segment.Start.Y);
        TResult numerator = GeometryScalar<TResult>.Checked(checked((dX * pX) + (dY * pY)));
        TResult denominator = GeometryScalar<TResult>.Checked(checked((dX * dX) + (dY * dY)));
        if (numerator <= TResult.Zero)
        {
            return segment.Start;
        }

        if (numerator >= denominator)
        {
            return segment.End;
        }

        return new(GeometryScalar<TResult>.Checked(checked(segment.Start.X + ExactDivide(checked(dX * numerator), denominator))), GeometryScalar<TResult>.Checked(checked(segment.Start.Y + ExactDivide(checked(dY * numerator), denominator))));
    }

    /// <summary>Returns distance in an explicit result type.</summary>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="point">The query point.</param>
    /// <returns>The distance.</returns>
    public TResult DistanceTo<TResult>(Point2D<T> point)
        where TResult : INumber<TResult> => (GeometryRational.Supports<T>() || typeof(T) != typeof(TResult)) ? ExactDistanceSquared(point).Root<TResult>() : point.ConvertChecked<TResult>().DistanceTo(ClosestPoint<TResult>(point));

    /// <summary>Returns squared distance without requiring a square root or integral projection.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>The squared distance.</returns>
    public T DistanceSquared(Point2D<T> point) => DistanceSquared<T>(point);

    /// <summary>Returns squared distance in an explicit result type.</summary>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="point">The query point.</param>
    /// <returns>The squared distance.</returns>
    public TResult DistanceSquared<TResult>(Point2D<T> point)
        where TResult : INumber<TResult> => (GeometryRational.Supports<T>() || typeof(T) != typeof(TResult)) ? ExactDistanceSquared(point).To<TResult>() : point.ConvertChecked<TResult>().DistanceSquared(ClosestPoint<TResult>(point));

    private bool OnSegment(Point2D<T> point) => point.X >= T.Min(Start.X, End.X) && point.X <= T.Max(Start.X, End.X)
        && point.Y >= T.Min(Start.Y, End.Y) && point.Y <= T.Max(Start.Y, End.Y);

    private static TResult ExactDivide<TResult>(TResult numerator, TResult denominator)
        where TResult : INumber<TResult>
    {
        TResult quotient = checked(numerator / denominator);
        if (checked(quotient * denominator) != numerator)
        {
            throw new ArithmeticException("The projection is not exact in the selected result type.");
        }

        return quotient;
    }

    private (GeometryRational X, GeometryRational Y) ExactClosestPoint(Point2D<T> point)
    {
        var sX = GeometryRational.From(Start.X);
        GeometryRational dX = GeometryRational.From(End.X) - sX;
        GeometryRational pX = GeometryRational.From(point.X) - sX;
        var sY = GeometryRational.From(Start.Y);
        GeometryRational dY = GeometryRational.From(End.Y) - sY;
        GeometryRational pY = GeometryRational.From(point.Y) - sY;
        GeometryRational numerator = (dX * pX) + (dY * pY);
        GeometryRational denominator = (dX * dX) + (dY * dY);
        if (numerator.Numerator.Sign <= 0 || denominator.Numerator.IsZero)
        {
            return (sX, sY);
        }

        if ((numerator - denominator).Numerator.Sign >= 0)
        {
            return (GeometryRational.From(End.X), GeometryRational.From(End.Y));
        }

        GeometryRational amount = numerator / denominator;
        return (sX + (dX * amount), sY + (dY * amount));
    }

    private GeometryRational ExactDistanceSquared(Point2D<T> point)
    {
        (GeometryRational X, GeometryRational Y) closest = ExactClosestPoint(point);
        GeometryRational dX = GeometryRational.From(point.X) - closest.X;
        GeometryRational dY = GeometryRational.From(point.Y) - closest.Y;
        return (dX * dX) + (dY * dY);
    }
}
