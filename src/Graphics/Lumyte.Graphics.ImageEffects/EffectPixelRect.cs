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

    internal bool IsEmpty => Width == 0 || Height == 0;

    internal EffectPixelRect Offset(int x, int y) => new(checked(X + x), checked(Y + y), Width, Height);

    internal EffectPixelRect OffsetInverse(int x, int y) => new(checked(X - x), checked(Y - y), Width, Height);

    internal EffectPixelRect Outset(int x, int y) => IsEmpty ? this : new(checked(X - x), checked(Y - y), checked(Width + (2 * x)), checked(Height + (2 * y)));

    internal EffectPixelRect Union(EffectPixelRect other)
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
