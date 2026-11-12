using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Half-open coordinate bounds that permit empty, reversed and infinite sentinels.</summary>
/// <remarks>Unlike the closed, finite <see cref="Rect2D{T}"/>, coordinates are not validated or normalized.
/// Maximum edges are excluded. Reversed, zero-area and NaN bounds contain no points.
/// Intersection preserves raw edges; union ignores empty operands. Arithmetic follows IEEE rules.</remarks>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
/// <param name="Left">The inclusive minimum x coordinate.</param>
/// <param name="Top">The inclusive minimum y coordinate.</param>
/// <param name="Right">The exclusive maximum x coordinate.</param>
/// <param name="Bottom">The exclusive maximum y coordinate.</param>
public readonly record struct Bounds2D<T>(T Left, T Top, T Right, T Bottom)
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Gets empty bounds represented by reversed infinite edges.</summary>
    public static Bounds2D<T> Empty => new(T.PositiveInfinity, T.PositiveInfinity, T.NegativeInfinity, T.NegativeInfinity);

    /// <summary>Gets bounds spanning all finite coordinates.</summary>
    /// <remarks>IEEE half-open comparisons intentionally also include negative infinity on either lower edge, but exclude positive infinity.</remarks>
    public static Bounds2D<T> Unbounded => new(T.NegativeInfinity, T.NegativeInfinity, T.PositiveInfinity, T.PositiveInfinity);

    /// <summary>Gets a value indicating whether the bounds contain no points.</summary>
    public bool IsEmpty => !(Left < Right && Top < Bottom);

    /// <summary>Gets the nonnegative horizontal extent, propagating NaN.</summary>
    public T Width => T.Max(T.Zero, Right - Left);

    /// <summary>Gets the nonnegative vertical extent, propagating NaN.</summary>
    public T Height => T.Max(T.Zero, Bottom - Top);

    /// <summary>Constructs bounds by adding dimensions without validation or normalization.</summary>
    /// <param name="x">The origin x coordinate.</param>
    /// <param name="y">The origin y coordinate.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The coordinate bounds in the specified precision.</returns>
    public static Bounds2D<T> FromOriginSize(T x, T y, T width, T height) => new(x, y, x + width, y + height);

    /// <summary>Tests half-open point containment.</summary>
    /// <param name="x">The point x coordinate.</param>
    /// <param name="y">The point y coordinate.</param>
    /// <returns>Whether the point belongs to these bounds.</returns>
    public bool Contains(T x, T y) => CoordinateGeometry.ContainsHalfOpenEdges((Left, Top, Right, Bottom), x, y);

    /// <summary>Intersects raw coordinate edges, retaining reversed empty results.</summary>
    /// <param name="other">The other bounds.</param>
    /// <returns>The intersection.</returns>
    public Bounds2D<T> Intersect(Bounds2D<T> other)
    {
        (T Left, T Top, T Right, T Bottom) edges = CoordinateGeometry.IntersectRawEdges((Left, Top, Right, Bottom), (other.Left, other.Top, other.Right, other.Bottom));
        return new(edges.Left, edges.Top, edges.Right, edges.Bottom);
    }

    /// <summary>Encloses nonempty operands, ignoring empty operands.</summary>
    /// <param name="other">The other bounds.</param>
    /// <returns>The enclosing bounds, or the other operand if this one is empty.</returns>
    public Bounds2D<T> Union(Bounds2D<T> other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        (T Left, T Top, T Right, T Bottom) edges = CoordinateGeometry.EncloseRawEdges((Left, Top, Right, Bottom), (other.Left, other.Top, other.Right, other.Bottom));
        return new(edges.Left, edges.Top, edges.Right, edges.Bottom);
    }
}
