using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed axis-aligned rectangle with nonnegative dimensions.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct Rect2D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Initializes a new instance of the <see cref="Rect2D{T}"/> struct.</summary>
    /// <param name="x">The minimum x coordinate.</param>
    /// <param name="y">The minimum y coordinate.</param>
    /// <param name="width">The nonnegative width.</param>
    /// <param name="height">The nonnegative height.</param>
    /// <exception cref="ArgumentOutOfRangeException">An input is nonfinite or a dimension is negative.</exception>
    /// <exception cref="OverflowException">A maximum coordinate is not finite.</exception>
    public Rect2D(T x, T y, T width, T height)
    {
        GeometryScalar<T>.Finite(x, nameof(x));
        GeometryScalar<T>.Finite(y, nameof(y));
        GeometryScalar<T>.NonNegative(width, nameof(width));
        GeometryScalar<T>.NonNegative(height, nameof(height));
        GeometryScalar<T>.Checked(x + width);
        GeometryScalar<T>.Checked(y + height);
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the minimum x coordinate.</summary>
    public T X { get; }

    /// <summary>Gets the minimum y coordinate.</summary>
    public T Y { get; }

    /// <summary>Gets the nonnegative width.</summary>
    public T Width { get; }

    /// <summary>Gets the nonnegative height.</summary>
    public T Height { get; }

    /// <summary>Gets the maximum x coordinate.</summary>
    public T Right => X + Width;

    /// <summary>Gets the maximum y coordinate.</summary>
    public T Bottom => Y + Height;

    /// <summary>Gets the minimum corner.</summary>
    public Point2D<T> Min => new(X, Y);

    /// <summary>Gets the maximum corner.</summary>
    public Point2D<T> Max => new(Right, Bottom);

    /// <summary>Tests containment including all boundaries and degenerate rectangles.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the point belongs to the closed rectangle.</returns>
    public bool Contains(Point2D<T> point) => point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>Tests containment excluding the maximum sides; zero dimensions contain no points.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the point belongs to the half-open rectangle.</returns>
    public bool ContainsHalfOpen(Point2D<T> point) => CoordinateGeometry.ContainsHalfOpenEdges((X, Y, Right, Bottom), point.X, point.Y);
}
