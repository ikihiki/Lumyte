using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed 2-dimensional segment, including coincident endpoints.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct Segment2D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
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
        if (Start == End)
        {
            return Start;
        }

        T dx = GeometryScalar<T>.Checked(End.X - Start.X);
        T dy = GeometryScalar<T>.Checked(End.Y - Start.Y);
        T px = GeometryScalar<T>.Checked(point.X - Start.X);
        T py = GeometryScalar<T>.Checked(point.Y - Start.Y);
        (T Numerator, T Denominator, int Exponent) amount = GeometryScalar<T>.Projection(dx, dy, T.Zero, px, py, T.Zero);
        return new Point2D<T>(GeometryScalar<T>.Lerp(Start.X, End.X, amount), GeometryScalar<T>.Lerp(Start.Y, End.Y, amount));
    }

    /// <summary>Returns the distance to the closest point on this closed segment.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>The Euclidean distance.</returns>
    /// <exception cref="OverflowException">A required difference or result is not finite.</exception>
    public T DistanceTo(Point2D<T> point) => point.DistanceTo(ClosestPoint(point));

    /// <summary>Tests closed intersection, including collinear overlap and endpoint contact.</summary>
    /// <param name="other">The other segment.</param>
    /// <returns>Whether the segments intersect in the selected floating-point precision.</returns>
    public bool Intersects(Segment2D<T> other)
    {
        T a = GeometryScalar<T>.Orientation(Start, End, other.Start);
        T b = GeometryScalar<T>.Orientation(Start, End, other.End);
        T c = GeometryScalar<T>.Orientation(other.Start, other.End, Start);
        T d = GeometryScalar<T>.Orientation(other.Start, other.End, End);
        return (a == T.Zero && OnSegment(other.Start))
            || (b == T.Zero && OnSegment(other.End))
            || (c == T.Zero && other.OnSegment(Start))
            || (d == T.Zero && other.OnSegment(End))
            || (((a < T.Zero && b > T.Zero) || (a > T.Zero && b < T.Zero))
                && ((c < T.Zero && d > T.Zero) || (c > T.Zero && d < T.Zero)));
    }

    private bool OnSegment(Point2D<T> point) => point.X >= T.Min(Start.X, End.X) && point.X <= T.Max(Start.X, End.X)
        && point.Y >= T.Min(Start.Y, End.Y) && point.Y <= T.Max(Start.Y, End.Y);
}
