using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed axis-aligned rectangle with nonnegative dimensions.</summary>
/// <typeparam name="T">The numeric coordinate scalar.</typeparam>
public readonly record struct Rect2D<T>
    where T : INumber<T>
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
        GeometryScalar<T>.Checked(checked(x + width));
        GeometryScalar<T>.Checked(checked(y + height));
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

    /// <summary>Gets a value indicating whether either extent is zero.</summary>
    public bool IsEmpty => Width == T.Zero || Height == T.Zero;

    /// <summary>Tests containment including all boundaries and degenerate rectangles.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the point belongs to the closed rectangle.</returns>
    public bool Contains(Point2D<T> point) => point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>Tests containment excluding the maximum sides; zero dimensions contain no points.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the point belongs to the half-open rectangle.</returns>
    public bool ContainsHalfOpen(Point2D<T> point) => Geometry2D.ContainsHalfOpenEdges((X, Y, Right, Bottom), point.X, point.Y);

    /// <summary>Translates the rectangle with checked coordinates and edges.</summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    /// <returns>The translated rectangle.</returns>
    public Rect2D<T> Offset(T x, T y) => new(GeometryScalar<T>.Checked(checked(X + x)), GeometryScalar<T>.Checked(checked(Y + y)), Width, Height);

    /// <summary>Subtracts a translation directly, without negating minimum integer values.</summary>
    /// <param name="x">The forward horizontal offset.</param>
    /// <param name="y">The forward vertical offset.</param>
    /// <returns>The inversely translated rectangle.</returns>
    public Rect2D<T> OffsetInverse(T x, T y) => new(GeometryScalar<T>.Checked(checked(X - x)), GeometryScalar<T>.Checked(checked(Y - y)), Width, Height);

    /// <summary>Expands closed bounds, including degenerate rectangles, by nonnegative margins, checking all intermediate arithmetic.</summary>
    /// <param name="x">The horizontal margin.</param>
    /// <param name="y">The vertical margin.</param>
    /// <returns>The expanded rectangle.</returns>
    public Rect2D<T> Outset(T x, T y)
    {
        GeometryScalar<T>.NonNegative(x, nameof(x));
        GeometryScalar<T>.NonNegative(y, nameof(y));
        T left = GeometryScalar<T>.Checked(checked(X - x));
        T top = GeometryScalar<T>.Checked(checked(Y - y));
        T width = GeometryScalar<T>.Checked(checked(Width + ((T.One + T.One) * x)));
        T height = GeometryScalar<T>.Checked(checked(Height + ((T.One + T.One) * y)));
        T right = GeometryScalar<T>.Checked(checked(Right + x));
        T bottom = GeometryScalar<T>.Checked(checked(Bottom + y));

        // Preserve the existing size arithmetic unless it would lose an edge.
        if (GeometryScalar<T>.Checked(checked(left + width)) < right)
        {
            width = RectangleRounding<T>.EnclosingExtent(left, right);
        }

        if (GeometryScalar<T>.Checked(checked(top + height)) < bottom)
        {
            height = RectangleRounding<T>.EnclosingExtent(top, bottom);
        }

        return new(left, top, width, height);
    }

    /// <summary>Computes a checked closed enclosure, including degenerate rectangles.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>A representable rectangle enclosing both inputs.</returns>
    /// <remarks>Extents are adjusted outward only when reconstructing an upper edge would otherwise exclude an input.</remarks>
    public Rect2D<T> Union(Rect2D<T> other)
    {
        // Compute both lower edges first, then each checked axis in the original overflow order.
        (T Left, T Top, T Right, T Bottom) lower = Geometry2D.EncloseBoundsEdges((X, Y, T.Zero, T.Zero), (other.X, other.Y, T.Zero, T.Zero));
        (T Left, T Top, T Right, T Bottom) horizontal = Geometry2D.EncloseBoundsEdges((T.Zero, T.Zero, checked(X + Width), T.Zero), (T.Zero, T.Zero, checked(other.X + other.Width), T.Zero));
        T width = RectangleRounding<T>.EnclosingExtent(lower.Left, horizontal.Right);
        (T Left, T Top, T Right, T Bottom) vertical = Geometry2D.EncloseBoundsEdges((T.Zero, T.Zero, T.Zero, checked(Y + Height)), (T.Zero, T.Zero, T.Zero, checked(other.Y + other.Height)));
        T height = RectangleRounding<T>.EnclosingExtent(lower.Top, vertical.Bottom);
        return new(lower.Left, lower.Top, width, height);
    }

    /// <summary>Creates the smallest closed bounds enclosing a nonempty sequence of points.</summary>
    /// <param name="points">The points to enclose; their order is immaterial.</param>
    /// <returns>The enclosing bounds, including zero-sized bounds for coincident points.</returns>
    /// <exception cref="ArgumentException">The point sequence is empty.</exception>
    /// <exception cref="OverflowException">An extent is not representable in the selected scalar.</exception>
    public static Rect2D<T> FromPoints(ReadOnlySpan<Point2D<T>> points)
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("At least one point is required.", nameof(points));
        }

        T minX = points[0].X;
        T maxX = minX;
        T minY = points[0].Y;
        T maxY = minY;
        for (int i = 1; i < points.Length; i++)
        {
            minX = T.Min(minX, points[i].X);
            maxX = T.Max(maxX, points[i].X);
            minY = T.Min(minY, points[i].Y);
            maxY = T.Max(maxY, points[i].Y);
        }

        return FromMinMax(new(minX, minY), new(maxX, maxY));
    }

    /// <summary>Attempts to enclose points without throwing for empty input or unrepresentable bounds.</summary>
    /// <param name="points">The points to enclose.</param>
    /// <param name="result">The enclosing bounds, or default on failure.</param>
    /// <returns>Whether the nonempty sequence has representable bounds.</returns>
    public static bool TryFromPoints(ReadOnlySpan<Point2D<T>> points, out Rect2D<T> result)
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

    /// <summary>Creates a rectangle from ordered corners with checked, enclosing extents.</summary>
    /// <param name="min">The minimum corner.</param>
    /// <param name="max">The maximum corner.</param>
    /// <returns>The rectangle, including both supplied corners after extent reconstruction.</returns>
    /// <remarks>Rounded extents are adjusted outward only when necessary; exact enclosures are not expanded.</remarks>
    /// <exception cref="OverflowException">An enclosing extent or reconstructed upper edge is not finite and representable.</exception>
    public static Rect2D<T> FromMinMax(Point2D<T> min, Point2D<T> max) => new(min.X, min.Y, RectangleRounding<T>.EnclosingExtent(min.X, max.X), RectangleRounding<T>.EnclosingExtent(min.Y, max.Y));

    /// <summary>Creates centered bounds; throws when a half extent is not representable.</summary>
    /// <param name="center">The center.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The rectangle.</returns>
    public static Rect2D<T> FromCenterSize(Point2D<T> center, T width, T height)
    {
        GeometryScalar<T>.NonNegative(width, nameof(width));
        GeometryScalar<T>.NonNegative(height, nameof(height));
        return new(GeometryScalar<T>.Checked(checked(center.X - ShapeScalar<T>.HalfExact(width))), GeometryScalar<T>.Checked(checked(center.Y - ShapeScalar<T>.HalfExact(height))), width, height);
    }

    /// <summary>Attempts validated construction without throwing for invalid values or overflow.</summary>
    /// <param name="x">The left coordinate.</param>
    /// <param name="y">The top coordinate.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="result">The rectangle, or default on failure.</param>
    /// <returns>Whether construction succeeded.</returns>
    public static bool TryCreate(T x, T y, T width, T height, out Rect2D<T> result)
    {
        try
        {
            result = new(x, y, width, height);
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

    /// <summary>Converts components using checked numeric conversion and validates the resulting bounds.</summary>
    /// <typeparam name="TOther">The destination scalar.</typeparam>
    /// <returns>The converted rectangle.</returns>
    public Rect2D<TOther> ConvertChecked<TOther>()
        where TOther : INumber<TOther>
        => new(TOther.CreateChecked(X), TOther.CreateChecked(Y), TOther.CreateChecked(Width), TOther.CreateChecked(Height));

    /// <summary>Attempts a closed intersection, including boundary-only contact.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <param name="result">The intersection, or default when the rectangles are disjoint.</param>
    /// <returns>Whether the rectangles share at least one point.</returns>
    public bool TryIntersect(Rect2D<T> other, out Rect2D<T> result)
    {
        T x = T.Max(X, other.X);
        T y = T.Max(Y, other.Y);
        T right = T.Min(Right, other.Right);
        T bottom = T.Min(Bottom, other.Bottom);
        if (right < x || bottom < y)
        {
            result = default;
            return false;
        }

        result = new(x, y, GeometryScalar<T>.Checked(checked(right - x)), GeometryScalar<T>.Checked(checked(bottom - y)));
        return true;
    }
}
