using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed axis-aligned box with ordered finite corners.</summary>
/// <typeparam name="T">The numeric coordinate scalar.</typeparam>
public readonly record struct AxisAlignedBox3D<T>
    where T : INumber<T>
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

    /// <summary>Attempts validated construction.</summary>
    /// <param name="min">The minimum corner.</param>
    /// <param name="max">The maximum corner.</param>
    /// <param name="result">The shape, or default on failure.</param>
    /// <returns>Whether construction succeeded.</returns>
    public static bool TryCreate(Point3D<T> min, Point3D<T> max, out AxisAlignedBox3D<T> result)
    {
        try
        {
            result = new(min, max);
            return true;
        }
        catch (ArgumentException)
        {
            result = default;
            return false;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    /// <summary>Converts components with checked numeric conversion.</summary>
    /// <typeparam name="TOther">The destination scalar.</typeparam>
    /// <returns>The validated converted shape.</returns>
    public AxisAlignedBox3D<TOther> ConvertChecked<TOther>()
        where TOther : INumber<TOther>
        => new(new(TOther.CreateChecked(Min.X), TOther.CreateChecked(Min.Y), TOther.CreateChecked(Min.Z)), new(TOther.CreateChecked(Max.X), TOther.CreateChecked(Max.Y), TOther.CreateChecked(Max.Z)));

    /// <summary>Creates the smallest closed bounds enclosing a nonempty sequence of points.</summary>
    /// <param name="points">The points to enclose; their order is immaterial.</param>
    /// <returns>The enclosing bounds, including zero-sized bounds for coincident points.</returns>
    /// <exception cref="ArgumentException">The point sequence is empty.</exception>
    public static AxisAlignedBox3D<T> FromPoints(ReadOnlySpan<Point3D<T>> points)
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("At least one point is required.", nameof(points));
        }

        T minX = points[0].X;
        T maxX = minX;
        T minY = points[0].Y;
        T maxY = minY;
        T minZ = points[0].Z;
        T maxZ = minZ;
        for (int i = 1; i < points.Length; i++)
        {
            minX = T.Min(minX, points[i].X);
            maxX = T.Max(maxX, points[i].X);
            minY = T.Min(minY, points[i].Y);
            maxY = T.Max(maxY, points[i].Y);
            minZ = T.Min(minZ, points[i].Z);
            maxZ = T.Max(maxZ, points[i].Z);
        }

        return new(new(minX, minY, minZ), new(maxX, maxY, maxZ));
    }

    /// <summary>Attempts to enclose points without throwing for empty input or unrepresentable bounds.</summary>
    /// <param name="points">The points to enclose.</param>
    /// <param name="result">The enclosing bounds, or default on failure.</param>
    /// <returns>Whether the nonempty sequence has representable bounds.</returns>
    public static bool TryFromPoints(ReadOnlySpan<Point3D<T>> points, out AxisAlignedBox3D<T> result)
    {
        try
        {
            result = FromPoints(points);
            return true;
        }
        catch (ArgumentException)
        {
            result = default;
            return false;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    /// <summary>Creates a box from ordered corners.</summary>
    /// <param name="min">The minimum corner.</param>
    /// <param name="max">The maximum corner.</param>
    /// <returns>The box.</returns>
    public static AxisAlignedBox3D<T> FromMinMax(Point3D<T> min, Point3D<T> max) => new(min, max);

    /// <summary>Creates a centered box; half extents must be representable.</summary>
    /// <param name="center">The center.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="depth">The depth.</param>
    /// <returns>The box.</returns>
    public static AxisAlignedBox3D<T> FromCenterSize(Point3D<T> center, T width, T height, T depth)
    {
        GeometryScalar<T>.NonNegative(width, nameof(width));
        GeometryScalar<T>.NonNegative(height, nameof(height));
        GeometryScalar<T>.NonNegative(depth, nameof(depth));
        T x = ShapeScalar<T>.HalfExact(width);
        T y = ShapeScalar<T>.HalfExact(height);
        T z = ShapeScalar<T>.HalfExact(depth);
        return new(new(GeometryScalar<T>.Checked(checked(center.X - x)), GeometryScalar<T>.Checked(checked(center.Y - y)), GeometryScalar<T>.Checked(checked(center.Z - z))), new(GeometryScalar<T>.Checked(checked(center.X + x)), GeometryScalar<T>.Checked(checked(center.Y + y)), GeometryScalar<T>.Checked(checked(center.Z + z))));
    }

    /// <summary>Encloses both closed boxes, including degenerate boxes.</summary>
    /// <param name="other">The other box.</param>
    /// <returns>The enclosing box.</returns>
    public AxisAlignedBox3D<T> Union(AxisAlignedBox3D<T> other) => new(new(T.Min(Min.X, other.Min.X), T.Min(Min.Y, other.Min.Y), T.Min(Min.Z, other.Min.Z)), new(T.Max(Max.X, other.Max.X), T.Max(Max.Y, other.Max.Y), T.Max(Max.Z, other.Max.Z)));

    /// <summary>Attempts a closed intersection; disjoint boxes have no representable closed intersection.</summary>
    /// <param name="other">The other box.</param>
    /// <param name="result">The intersection, or default on failure.</param>
    /// <returns>Whether the boxes overlap or touch.</returns>
    public bool TryIntersect(AxisAlignedBox3D<T> other, out AxisAlignedBox3D<T> result) => TryCreate(new(T.Max(Min.X, other.Min.X), T.Max(Min.Y, other.Min.Y), T.Max(Min.Z, other.Min.Z)), new(T.Min(Max.X, other.Max.X), T.Min(Max.Y, other.Max.Y), T.Min(Max.Z, other.Max.Z)), out result);
}
