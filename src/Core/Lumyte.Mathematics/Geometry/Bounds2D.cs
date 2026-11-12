using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Half-open coordinate bounds that permit empty, reversed and infinite sentinels.</summary>
/// <remarks>Unlike the closed, finite <see cref="Rect2D{T}"/>, coordinates are not validated or normalized.
/// Maximum edges are excluded. Reversed, zero-area and NaN bounds contain no points.
/// Intersection preserves raw edges; union ignores empty operands. Arithmetic is checked for bounded integer scalars; IEEE arithmetic preserves infinities and NaN.</remarks>
/// <typeparam name="T">The numeric coordinate scalar.</typeparam>
/// <param name="Left">The inclusive minimum x coordinate.</param>
/// <param name="Top">The inclusive minimum y coordinate.</param>
/// <param name="Right">The exclusive maximum x coordinate.</param>
/// <param name="Bottom">The exclusive maximum y coordinate.</param>
public readonly record struct Bounds2D<T>(T Left, T Top, T Right, T Bottom)
    where T : INumber<T>
{
    /// <summary>Gets empty bounds; IEEE scalars use reversed infinities, other scalars use the default anchor.</summary>
    public static Bounds2D<T> Empty => TryGetUnbounded(out _) ? new(T.CreateChecked(double.PositiveInfinity), T.CreateChecked(double.PositiveInfinity), T.CreateChecked(double.NegativeInfinity), T.CreateChecked(double.NegativeInfinity)) : default;

    /// <summary>Gets bounds spanning all finite coordinates.</summary>
    /// <remarks>IEEE half-open comparisons intentionally also include negative infinity on either lower edge, but exclude positive infinity.</remarks>
    public static Bounds2D<T> Unbounded => TryGetUnbounded(out Bounds2D<T> result) ? result : throw new NotSupportedException("Unbounded bounds require an IEEE scalar with infinities.");

    /// <summary>Gets a value indicating whether the bounds contain no points.</summary>
    public bool IsEmpty => !(Left < Right && Top < Bottom);

    /// <summary>Gets the nonnegative horizontal extent, propagating NaN.</summary>
    public T Width => Right > Left ? checked(Right - Left) : T.IsNaN(Right) || T.IsNaN(Left) ? Right - Left : T.Zero;

    /// <summary>Gets the nonnegative vertical extent, propagating NaN.</summary>
    public T Height => Bottom > Top ? checked(Bottom - Top) : T.IsNaN(Bottom) || T.IsNaN(Top) ? Bottom - Top : T.Zero;

    /// <summary>Constructs bounds by adding dimensions without validation or normalization.</summary>
    /// <param name="x">The origin x coordinate.</param>
    /// <param name="y">The origin y coordinate.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The coordinate bounds in the specified precision.</returns>
    public static Bounds2D<T> FromOriginSize(T x, T y, T width, T height) => new(x, y, checked(x + width), checked(y + height));

    /// <summary>Tests half-open point containment.</summary>
    /// <param name="x">The point x coordinate.</param>
    /// <param name="y">The point y coordinate.</param>
    /// <returns>Whether the point belongs to these bounds.</returns>
    public bool Contains(T x, T y) => Geometry2D.ContainsHalfOpenEdges((Left, Top, Right, Bottom), x, y);

    /// <summary>Intersects raw coordinate edges, retaining reversed empty results.</summary>
    /// <param name="other">The other bounds.</param>
    /// <returns>The intersection.</returns>
    public Bounds2D<T> Intersect(Bounds2D<T> other)
    {
        (T Left, T Top, T Right, T Bottom) edges = Geometry2D.IntersectBoundsEdges((Left, Top, Right, Bottom), (other.Left, other.Top, other.Right, other.Bottom));
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

        (T Left, T Top, T Right, T Bottom) edges = Geometry2D.EncloseBoundsEdges((Left, Top, Right, Bottom), (other.Left, other.Top, other.Right, other.Bottom));
        return new(edges.Left, edges.Top, edges.Right, edges.Bottom);
    }

    /// <summary>Attempts to create genuinely unbounded IEEE bounds; other scalars have no infinity sentinel.</summary>
    /// <param name="result">The unbounded bounds, or default when unsupported.</param>
    /// <returns>Whether the scalar supports IEEE infinities.</returns>
    public static bool TryGetUnbounded(out Bounds2D<T> result)
    {
        if (typeof(T) == typeof(Half) || typeof(T) == typeof(float) || typeof(T) == typeof(double))
        {
            T negative = T.CreateChecked(double.NegativeInfinity);
            T positive = T.CreateChecked(double.PositiveInfinity);
            result = new(negative, negative, positive, positive);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Creates raw bounds from two corners without ordering validation.</summary>
    /// <param name="min">The lower corner.</param>
    /// <param name="max">The upper corner.</param>
    /// <returns>The raw bounds.</returns>
    public static Bounds2D<T> FromMinMax(Point2D<T> min, Point2D<T> max) => new(min.X, min.Y, max.X, max.Y);

    /// <summary>Creates finite centered bounds using validated rectangle arithmetic.</summary>
    /// <param name="center">The center.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The bounds.</returns>
    public static Bounds2D<T> FromCenterSize(Point2D<T> center, T width, T height)
    {
        var rect = Rect2D<T>.FromCenterSize(center, width, height);
        return new(rect.X, rect.Y, rect.Right, rect.Bottom);
    }

    /// <summary>Attempts finite ordered edge construction; zero extents are allowed.</summary>
    /// <param name="left">The minimum x coordinate.</param>
    /// <param name="top">The minimum y coordinate.</param>
    /// <param name="right">The maximum x coordinate.</param>
    /// <param name="bottom">The maximum y coordinate.</param>
    /// <param name="result">The bounds, or default on failure.</param>
    /// <returns>Whether the edges are finite and ordered.</returns>
    public static bool TryCreate(T left, T top, T right, T bottom, out Bounds2D<T> result)
    {
        if (!T.IsFinite(left) || !T.IsFinite(top) || !T.IsFinite(right) || !T.IsFinite(bottom) || left > right || top > bottom)
        {
            result = default;
            return false;
        }

        result = new(left, top, right, bottom);
        return true;
    }

    /// <summary>Converts raw edges using checked numeric conversion without normalization.</summary>
    /// <typeparam name="TOther">The destination scalar.</typeparam>
    /// <returns>The converted bounds.</returns>
    public Bounds2D<TOther> ConvertChecked<TOther>()
        where TOther : INumber<TOther>
        => new(TOther.CreateChecked(Left), TOther.CreateChecked(Top), TOther.CreateChecked(Right), TOther.CreateChecked(Bottom));
}
