using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed ball with a finite center and nonnegative radius.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct Sphere3D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Initializes a new instance of the <see cref="Sphere3D{T}"/> struct.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The finite nonnegative radius.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is nonfinite or negative.</exception>
    public Sphere3D(Point3D<T> center, T radius)
    {
        GeometryScalar<T>.NonNegative(radius, nameof(radius));
        Center = center;
        Radius = radius;
    }

    /// <summary>Gets the center.</summary>
    public Point3D<T> Center { get; }

    /// <summary>Gets the radius.</summary>
    public T Radius { get; }

    /// <summary>Tests containment including the boundary; zero radius contains only the center.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the distance to the center does not exceed the radius.</returns>
    /// <exception cref="OverflowException">A required difference or distance is not finite.</exception>
    public bool Contains(Point3D<T> point) => Center.DistanceTo(point) <= Radius;
}
