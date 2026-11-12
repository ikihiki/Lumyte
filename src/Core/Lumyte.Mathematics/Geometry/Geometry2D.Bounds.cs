using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides two-dimensional geometry operations with explicit numeric and boundary policies.</summary>
public static partial class Geometry2D
{
    /// <summary>Tests raw half-open edges without validation, normalization or extent arithmetic.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="edges">The inclusive lower and exclusive upper edges.</param>
    /// <param name="pointX">The query x coordinate.</param>
    /// <param name="pointY">The query y coordinate.</param>
    /// <returns>Whether both lower-inclusive, upper-exclusive comparisons hold.</returns>
    /// <remarks>IEEE comparisons reject NaN and positive-infinite upper boundaries but include negative-infinite lower boundaries.</remarks>
    public static bool ContainsHalfOpenEdges<T>((T Left, T Top, T Right, T Bottom) edges, T pointX, T pointY)
        where T : INumber<T>
        => pointX >= edges.Left && pointY >= edges.Top && pointX < edges.Right && pointY < edges.Bottom;

    /// <summary>Intersects raw edges, retaining reversed results and propagating IEEE NaN.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle's edges.</param>
    /// <param name="second">The second rectangle's edges.</param>
    /// <returns>Maximum lower and minimum upper edges, without empty handling or extent arithmetic.</returns>
    public static (T Left, T Top, T Right, T Bottom) IntersectBoundsEdges<T>((T Left, T Top, T Right, T Bottom) first, (T Left, T Top, T Right, T Bottom) second)
        where T : INumber<T>
        => (T.Max(first.Left, second.Left), T.Max(first.Top, second.Top), T.Min(first.Right, second.Right), T.Min(first.Bottom, second.Bottom));

    /// <summary>Encloses raw edges without treating any operand as empty.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle's edges.</param>
    /// <param name="second">The second rectangle's edges.</param>
    /// <returns>Minimum lower and maximum upper edges, propagating IEEE NaN.</returns>
    /// <remarks>Callers own empty policies and checked edge or extent arithmetic.</remarks>
    public static (T Left, T Top, T Right, T Bottom) EncloseBoundsEdges<T>((T Left, T Top, T Right, T Bottom) first, (T Left, T Top, T Right, T Bottom) second)
        where T : INumber<T>
        => (T.Min(first.Left, second.Left), T.Min(first.Top, second.Top), T.Max(first.Right, second.Right), T.Max(first.Bottom, second.Bottom));

    /// <summary>Tests half-open containment; nonpositive dimensions contain no points.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="pointX">The query x coordinate.</param>
    /// <param name="pointY">The query y coordinate.</param>
    /// <returns>Whether the query lies inside the half-open bounds.</returns>
    public static bool ContainsHalfOpenBounds<T>(T x, T y, T width, T height, T pointX, T pointY)
        where T : INumber<T>
        => width > T.Zero && height > T.Zero && ContainsHalfOpenEdges((x, y, checked(x + width), checked(y + height)), pointX, pointY);

    /// <summary>Intersects bounds, retaining the maximum lower-edge anchor for disjoint inputs.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle.</param>
    /// <param name="second">The second rectangle.</param>
    /// <returns>The intersection with each negative extent clamped to zero.</returns>
    public static (T X, T Y, T Width, T Height) IntersectBoundsClamped<T>((T X, T Y, T Width, T Height) first, (T X, T Y, T Width, T Height) second)
        where T : INumber<T>
        => IntersectBoundsClampedEdges((first.X, first.Y, checked(first.X + first.Width), checked(first.Y + first.Height)), (second.X, second.Y, checked(second.X + second.Width), checked(second.Y + second.Height)));

    /// <summary>Intersects edge-form bounds without reconstructing upper edges from rounded extents.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle's edges.</param>
    /// <param name="second">The second rectangle's edges.</param>
    /// <returns>The intersection with each negative extent clamped to zero.</returns>
    public static (T X, T Y, T Width, T Height) IntersectBoundsClampedEdges<T>((T Left, T Top, T Right, T Bottom) first, (T Left, T Top, T Right, T Bottom) second)
        where T : INumber<T>
    {
        (T Left, T Top, T Right, T Bottom) edges = IntersectBoundsEdges(first, second);
        return (edges.Left, edges.Top, Extent(edges.Left, edges.Right), Extent(edges.Top, edges.Bottom));
    }

    /// <summary>Computes bounding union, treating zero-width or zero-height inputs as absent.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle.</param>
    /// <param name="second">The second rectangle.</param>
    /// <returns>The bounding rectangle, or the other input when one is empty.</returns>
    /// <remarks>Integer callers needing validated storage and checked arithmetic should use <see cref="Rect2D{T}"/>.</remarks>
    public static (T X, T Y, T Width, T Height) UnionBoundsIgnoringZeroArea<T>((T X, T Y, T Width, T Height) first, (T X, T Y, T Width, T Height) second)
        where T : INumber<T>
    {
        if (first.Width == T.Zero || first.Height == T.Zero)
        {
            return second;
        }

        if (second.Width == T.Zero || second.Height == T.Zero)
        {
            return first;
        }

        (T Left, T Top, T Right, T Bottom) edges = EncloseBoundsEdges((first.X, first.Y, checked(first.X + first.Width), checked(first.Y + first.Height)), (second.X, second.Y, checked(second.X + second.Width), checked(second.Y + second.Height)));
        return (edges.Left, edges.Top, checked(edges.Right - edges.Left), checked(edges.Bottom - edges.Top));
    }

    private static T Extent<T>(T lower, T upper)
        where T : INumber<T> => upper > lower ? checked(upper - lower) : T.IsNaN(lower) || T.IsNaN(upper) ? upper - lower : T.Zero;
}
