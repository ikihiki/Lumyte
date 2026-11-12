using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed axis-aligned box with ordered finite corners.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct AxisAlignedBox3D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Initializes a new instance of the <see cref="AxisAlignedBox3D{T}"/> struct.</summary>
    /// <param name="min">The minimum corner.</param>
    /// <param name="max">The maximum corner.</param>
    /// <exception cref="ArgumentOutOfRangeException">A minimum exceeds a maximum.</exception>
    public AxisAlignedBox3D(Point3D<T> min, Point3D<T> max)
    {
        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum coordinates must not precede minimum coordinates.");
        }

        Min = min;
        Max = max;
    }

    /// <summary>Gets the minimum corner.</summary>
    public Point3D<T> Min { get; }

    /// <summary>Gets the maximum corner.</summary>
    public Point3D<T> Max { get; }

    /// <summary>Tests containment including every boundary.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the point belongs to the closed box.</returns>
    public bool Contains(Point3D<T> point) => point.X >= Min.X && point.X <= Max.X
        && point.Y >= Min.Y && point.Y <= Max.Y && point.Z >= Min.Z && point.Z <= Max.Z;
}
