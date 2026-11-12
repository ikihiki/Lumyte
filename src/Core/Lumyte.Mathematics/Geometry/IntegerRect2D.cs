using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A half-open integer rectangle with nonnegative extents and checked edges.</summary>
/// <typeparam name="T">The integer coordinate scalar; arithmetic remains in this type.</typeparam>
public readonly record struct IntegerRect2D<T>
    where T : IBinaryInteger<T>
{
    /// <summary>Initializes a new instance of the <see cref="IntegerRect2D{T}"/> struct.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The nonnegative width.</param>
    /// <param name="height">The nonnegative height.</param>
    public IntegerRect2D(T x, T y, T width, T height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        _ = checked(x + width);
        _ = checked(y + height);
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the left edge.</summary>
    public T X { get; }

    /// <summary>Gets the top edge.</summary>
    public T Y { get; }

    /// <summary>Gets the nonnegative width.</summary>
    public T Width { get; }

    /// <summary>Gets the nonnegative height.</summary>
    public T Height { get; }

    /// <summary>Gets a value indicating whether either extent is zero.</summary>
    public bool IsEmpty => Width == T.Zero || Height == T.Zero;

    /// <summary>Translates the rectangle with checked coordinates and edges.</summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    /// <returns>The translated rectangle.</returns>
    public IntegerRect2D<T> Offset(T x, T y) => new(checked(X + x), checked(Y + y), Width, Height);

    /// <summary>Subtracts a translation directly, without negating minimum integer values.</summary>
    /// <param name="x">The forward horizontal offset.</param>
    /// <param name="y">The forward vertical offset.</param>
    /// <returns>The inversely translated rectangle.</returns>
    public IntegerRect2D<T> OffsetInverse(T x, T y) => new(checked(X - x), checked(Y - y), Width, Height);

    /// <summary>Expands nonempty bounds by nonnegative margins, checking all intermediate arithmetic.</summary>
    /// <param name="x">The horizontal margin.</param>
    /// <param name="y">The vertical margin.</param>
    /// <returns>The expanded rectangle, or the unchanged empty rectangle.</returns>
    public IntegerRect2D<T> Outset(T x, T y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        return IsEmpty ? this : new(checked(X - x), checked(Y - y), checked(Width + ((T.One + T.One) * x)), checked(Height + ((T.One + T.One) * y)));
    }

    /// <summary>Computes a checked bounding union, treating empty rectangles as absent.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The union; when both inputs are empty, the second anchor wins.</returns>
    public IntegerRect2D<T> Union(IntegerRect2D<T> other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        // Compute both lower edges first, then each checked axis in the original overflow order.
        (T Left, T Top, T Right, T Bottom) lower = CoordinateGeometry.EncloseRawEdges((X, Y, T.Zero, T.Zero), (other.X, other.Y, T.Zero, T.Zero));
        (T Left, T Top, T Right, T Bottom) horizontal = CoordinateGeometry.EncloseRawEdges((T.Zero, T.Zero, checked(X + Width), T.Zero), (T.Zero, T.Zero, checked(other.X + other.Width), T.Zero));
        T width = checked(horizontal.Right - lower.Left);
        (T Left, T Top, T Right, T Bottom) vertical = CoordinateGeometry.EncloseRawEdges((T.Zero, T.Zero, T.Zero, checked(Y + Height)), (T.Zero, T.Zero, T.Zero, checked(other.Y + other.Height)));
        T height = checked(vertical.Bottom - lower.Top);
        return new(lower.Left, lower.Top, width, height);
    }
}
