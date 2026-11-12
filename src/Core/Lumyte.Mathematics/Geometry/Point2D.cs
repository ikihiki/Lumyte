using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A finite 2-dimensional coordinate; the default value is the origin.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct Point2D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Initializes a new instance of the <see cref="Point2D{T}"/> struct.</summary>
    /// <param name="x">The finite x coordinate.</param>
    /// <param name="y">The finite y coordinate.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    public Point2D(T x, T y)
    {
        GeometryScalar<T>.Finite(x, nameof(x));
        GeometryScalar<T>.Finite(y, nameof(y));
        X = x;
        Y = y;
    }

    /// <summary>Gets the x coordinate.</summary>
    public T X { get; }

    /// <summary>Gets the y coordinate.</summary>
    public T Y { get; }

    /// <summary>Returns the Euclidean distance without unnecessary squared overflow.</summary>
    /// <param name="other">The other point.</param>
    /// <returns>The distance in the selected precision.</returns>
    /// <exception cref="OverflowException">A required difference or result is not finite.</exception>
    public T DistanceTo(Point2D<T> other) => GeometryScalar<T>.Hypot(GeometryScalar<T>.Checked(X - other.X), GeometryScalar<T>.Checked(Y - other.Y));
}
