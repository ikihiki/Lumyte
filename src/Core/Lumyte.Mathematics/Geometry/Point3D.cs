using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A finite 3-dimensional coordinate; the default value is the origin.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct Point3D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Initializes a new instance of the <see cref="Point3D{T}"/> struct.</summary>
    /// <param name="x">The finite x coordinate.</param>
    /// <param name="y">The finite y coordinate.</param>
    /// <param name="z">The finite z coordinate.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    public Point3D(T x, T y, T z)
    {
        GeometryScalar<T>.Finite(x, nameof(x));
        GeometryScalar<T>.Finite(y, nameof(y));
        GeometryScalar<T>.Finite(z, nameof(z));
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the x coordinate.</summary>
    public T X { get; }

    /// <summary>Gets the y coordinate.</summary>
    public T Y { get; }

    /// <summary>Gets the z coordinate.</summary>
    public T Z { get; }

    /// <summary>Returns the Euclidean distance without unnecessary squared overflow.</summary>
    /// <param name="other">The other point.</param>
    /// <returns>The distance in the selected precision.</returns>
    /// <exception cref="OverflowException">A required difference or result is not finite.</exception>
    public T DistanceTo(Point3D<T> other) => GeometryScalar<T>.Hypot(GeometryScalar<T>.Checked(X - other.X), GeometryScalar<T>.Checked(Y - other.Y), GeometryScalar<T>.Checked(Z - other.Z));
}
