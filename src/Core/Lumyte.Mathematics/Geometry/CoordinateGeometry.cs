using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides unchecked coordinate operations for domain rectangles with caller-owned validation.</summary>
/// <remarks>These operations preserve IEEE arithmetic, empty anchors and half-open boundary conventions without constructing validated geometry values.</remarks>
public static class CoordinateGeometry
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
    public static (T Left, T Top, T Right, T Bottom) IntersectRawEdges<T>((T Left, T Top, T Right, T Bottom) first, (T Left, T Top, T Right, T Bottom) second)
        where T : INumber<T>
        => (T.Max(first.Left, second.Left), T.Max(first.Top, second.Top), T.Min(first.Right, second.Right), T.Min(first.Bottom, second.Bottom));

    /// <summary>Encloses raw edges without treating any operand as empty.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle's edges.</param>
    /// <param name="second">The second rectangle's edges.</param>
    /// <returns>Minimum lower and maximum upper edges, propagating IEEE NaN.</returns>
    /// <remarks>Callers own empty policies and checked edge or extent arithmetic.</remarks>
    public static (T Left, T Top, T Right, T Bottom) EncloseRawEdges<T>((T Left, T Top, T Right, T Bottom) first, (T Left, T Top, T Right, T Bottom) second)
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
    public static bool ContainsHalfOpen<T>(T x, T y, T width, T height, T pointX, T pointY)
        where T : INumber<T>
        => width > T.Zero && height > T.Zero && ContainsHalfOpenEdges((x, y, x + width, y + height), pointX, pointY);

    /// <summary>Intersects bounds, retaining the maximum lower-edge anchor for disjoint inputs.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle.</param>
    /// <param name="second">The second rectangle.</param>
    /// <returns>The intersection with each negative extent clamped to zero.</returns>
    public static (T X, T Y, T Width, T Height) Intersect<T>((T X, T Y, T Width, T Height) first, (T X, T Y, T Width, T Height) second)
        where T : INumber<T>
        => IntersectEdges((first.X, first.Y, first.X + first.Width, first.Y + first.Height), (second.X, second.Y, second.X + second.Width, second.Y + second.Height));

    /// <summary>Intersects edge-form bounds without reconstructing upper edges from rounded extents.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle's edges.</param>
    /// <param name="second">The second rectangle's edges.</param>
    /// <returns>The intersection with each negative extent clamped to zero.</returns>
    public static (T X, T Y, T Width, T Height) IntersectEdges<T>((T Left, T Top, T Right, T Bottom) first, (T Left, T Top, T Right, T Bottom) second)
        where T : INumber<T>
    {
        (T Left, T Top, T Right, T Bottom) edges = IntersectRawEdges(first, second);
        return (edges.Left, edges.Top, T.Max(T.Zero, edges.Right - edges.Left), T.Max(T.Zero, edges.Bottom - edges.Top));
    }

    /// <summary>Computes bounding union, treating zero-width or zero-height inputs as absent.</summary>
    /// <typeparam name="T">The coordinate scalar.</typeparam>
    /// <param name="first">The first rectangle.</param>
    /// <param name="second">The second rectangle.</param>
    /// <returns>The bounding rectangle, or the other input when one is empty.</returns>
    /// <remarks>Integer callers needing validated storage and checked arithmetic should use <see cref="IntegerRect2D{T}"/>.</remarks>
    public static (T X, T Y, T Width, T Height) Union<T>((T X, T Y, T Width, T Height) first, (T X, T Y, T Width, T Height) second)
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

        (T Left, T Top, T Right, T Bottom) edges = EncloseRawEdges((first.X, first.Y, first.X + first.Width, first.Y + first.Height), (second.X, second.Y, second.X + second.Width, second.Y + second.Height));
        return (edges.Left, edges.Top, edges.Right - edges.Left, edges.Bottom - edges.Top);
    }
}
