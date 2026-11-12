using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed 3-dimensional segment, including coincident endpoints.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct Segment3D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Initializes a new instance of the <see cref="Segment3D{T}"/> struct.</summary>
    /// <param name="start">The first endpoint.</param>
    /// <param name="end">The second endpoint.</param>
    public Segment3D(Point3D<T> start, Point3D<T> end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Gets the first endpoint.</summary>
    public Point3D<T> Start { get; }

    /// <summary>Gets the second endpoint.</summary>
    public Point3D<T> End { get; }

    /// <summary>Gets the Euclidean endpoint distance.</summary>
    public T Length => Start.DistanceTo(End);

    /// <summary>Finds the closest point using a scaled, endpoint-clamped projection.</summary>
    /// <param name="point">The point to project.</param>
    /// <returns>The closest point, or the endpoint for a zero-length segment.</returns>
    /// <exception cref="OverflowException">A required difference or result is not finite.</exception>
    public Point3D<T> ClosestPoint(Point3D<T> point)
    {
        if (Start == End)
        {
            return Start;
        }

        T dx = GeometryScalar<T>.Checked(End.X - Start.X);
        T dy = GeometryScalar<T>.Checked(End.Y - Start.Y);
        T dz = GeometryScalar<T>.Checked(End.Z - Start.Z);
        T px = GeometryScalar<T>.Checked(point.X - Start.X);
        T py = GeometryScalar<T>.Checked(point.Y - Start.Y);
        T pz = GeometryScalar<T>.Checked(point.Z - Start.Z);
        (T Numerator, T Denominator, int Exponent) amount = GeometryScalar<T>.Projection(dx, dy, dz, px, py, pz);
        return new Point3D<T>(GeometryScalar<T>.Lerp(Start.X, End.X, amount), GeometryScalar<T>.Lerp(Start.Y, End.Y, amount), GeometryScalar<T>.Lerp(Start.Z, End.Z, amount));
    }

    /// <summary>Returns the distance to the closest point on this closed segment.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>The Euclidean distance.</returns>
    /// <exception cref="OverflowException">A required difference or result is not finite.</exception>
    public T DistanceTo(Point3D<T> point) => point.DistanceTo(ClosestPoint(point));
}
