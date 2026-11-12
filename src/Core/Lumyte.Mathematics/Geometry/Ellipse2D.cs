using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed axis-aligned ellipse with nonnegative semiaxes, including line and point degeneracies.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct Ellipse2D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Initializes a new instance of the <see cref="Ellipse2D{T}"/> struct.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radiusX">The finite nonnegative horizontal semiaxis.</param>
    /// <param name="radiusY">The finite nonnegative vertical semiaxis.</param>
    /// <exception cref="ArgumentOutOfRangeException">A semiaxis is nonfinite or negative.</exception>
    public Ellipse2D(Point2D<T> center, T radiusX, T radiusY)
    {
        GeometryScalar<T>.NonNegative(radiusX, nameof(radiusX));
        GeometryScalar<T>.NonNegative(radiusY, nameof(radiusY));
        Center = center;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    /// <summary>Gets the center.</summary>
    public Point2D<T> Center { get; }

    /// <summary>Gets the horizontal semiaxis.</summary>
    public T RadiusX { get; }

    /// <summary>Gets the vertical semiaxis.</summary>
    public T RadiusY { get; }

    /// <summary>Tests containment including the boundary and degenerate semiaxes.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the point belongs to the ellipse.</returns>
    /// <exception cref="OverflowException">A required coordinate difference is not finite.</exception>
    public bool Contains(Point2D<T> point)
    {
        T x = T.Abs(GeometryScalar<T>.Checked(point.X - Center.X));
        T y = T.Abs(GeometryScalar<T>.Checked(point.Y - Center.Y));
        if (x > RadiusX || y > RadiusY)
        {
            return false;
        }

        if (RadiusX == T.Zero || RadiusY == T.Zero)
        {
            return true;
        }

        return GeometryScalar<T>.Hypot(x / RadiusX, y / RadiusY) <= T.One;
    }
}
