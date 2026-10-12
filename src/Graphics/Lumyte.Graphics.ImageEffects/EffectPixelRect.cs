namespace Lumyte.Graphics.ImageEffects;

/// <summary>A finite half-open rectangle in the common device-pixel coordinate system.</summary>
public readonly record struct EffectPixelRect
{
    /// <summary>Initializes a new instance of the <see cref="EffectPixelRect"/> struct with validated nonnegative size and finite integer edges.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The nonnegative width.</param>
    /// <param name="height">The nonnegative height.</param>
    public EffectPixelRect(int x, int y, int width, int height)
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
    public int X { get; }

    /// <summary>Gets the top edge.</summary>
    public int Y { get; }

    /// <summary>Gets the nonnegative width.</summary>
    public int Width { get; }

    /// <summary>Gets the nonnegative height.</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether either dimension is zero.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Translates the rectangle, rejecting integer overflow.</summary>
    /// <param name="x">The horizontal device-pixel offset.</param>
    /// <param name="y">The vertical device-pixel offset.</param>
    /// <returns>The translated rectangle.</returns>
    public EffectPixelRect Offset(int x, int y) => new(checked(X + x), checked(Y + y), Width, Height);

    /// <summary>Applies the inverse of a translation, rejecting integer overflow.</summary>
    /// <param name="x">The forward horizontal offset.</param>
    /// <param name="y">The forward vertical offset.</param>
    /// <returns>The inversely translated rectangle.</returns>
    public EffectPixelRect OffsetInverse(int x, int y) => new(checked(X - x), checked(Y - y), Width, Height);

    /// <summary>Expands nonempty bounds by nonnegative margins on both sides of each axis.</summary>
    /// <param name="x">The horizontal margin.</param>
    /// <param name="y">The vertical margin.</param>
    /// <returns>The expanded rectangle, or the unchanged empty rectangle.</returns>
    public EffectPixelRect Outset(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        return IsEmpty ? this : new(checked(X - x), checked(Y - y), checked(Width + (2 * x)), checked(Height + (2 * y)));
    }

    /// <summary>Returns the smallest rectangle containing both finite inputs.</summary>
    /// <param name="other">The second rectangle.</param>
    /// <returns>The bounding union, treating empty rectangles as absent.</returns>
    public EffectPixelRect Union(EffectPixelRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        int x = Math.Min(X, other.X);
        int y = Math.Min(Y, other.Y);
        return new(x, y, checked(Math.Max(X + Width, other.X + other.Width) - x), checked(Math.Max(Y + Height, other.Y + other.Height) - y));
    }
}
