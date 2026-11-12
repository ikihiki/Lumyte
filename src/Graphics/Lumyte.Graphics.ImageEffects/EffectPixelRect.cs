using Lumyte.Mathematics.Geometry;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>A finite half-open rectangle in the common device-pixel coordinate system.</summary>
public readonly record struct EffectPixelRect
{
    private readonly IntegerRect2D<int> _bounds;

    /// <summary>Initializes a new instance of the <see cref="EffectPixelRect"/> struct with validated nonnegative size and finite integer edges.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The nonnegative width.</param>
    /// <param name="height">The nonnegative height.</param>
    public EffectPixelRect(int x, int y, int width, int height)
        : this(new IntegerRect2D<int>(x, y, width, height))
    {
    }

    private EffectPixelRect(IntegerRect2D<int> bounds)
    {
        _bounds = bounds;
    }

    /// <summary>Gets the left edge.</summary>
    public int X => _bounds.X;

    /// <summary>Gets the top edge.</summary>
    public int Y => _bounds.Y;

    /// <summary>Gets the nonnegative width.</summary>
    public int Width => _bounds.Width;

    /// <summary>Gets the nonnegative height.</summary>
    public int Height => _bounds.Height;

    /// <summary>Gets a value indicating whether either dimension is zero.</summary>
    public bool IsEmpty => _bounds.IsEmpty;

    /// <summary>Translates the rectangle, rejecting integer overflow.</summary>
    /// <param name="x">The horizontal device-pixel offset.</param>
    /// <param name="y">The vertical device-pixel offset.</param>
    /// <returns>The translated rectangle.</returns>
    public EffectPixelRect Offset(int x, int y) => new(_bounds.Offset(x, y));

    /// <summary>Applies the inverse of a translation, rejecting integer overflow.</summary>
    /// <param name="x">The forward horizontal offset.</param>
    /// <param name="y">The forward vertical offset.</param>
    /// <returns>The inversely translated rectangle.</returns>
    public EffectPixelRect OffsetInverse(int x, int y) => new(_bounds.OffsetInverse(x, y));

    /// <summary>Expands nonempty bounds by nonnegative margins on both sides of each axis.</summary>
    /// <param name="x">The horizontal margin.</param>
    /// <param name="y">The vertical margin.</param>
    /// <returns>The expanded rectangle, or the unchanged empty rectangle.</returns>
    public EffectPixelRect Outset(int x, int y) => new(_bounds.Outset(x, y));

    /// <summary>Returns the smallest rectangle containing both finite inputs.</summary>
    /// <param name="other">The second rectangle.</param>
    /// <returns>The bounding union, treating empty rectangles as absent.</returns>
    public EffectPixelRect Union(EffectPixelRect other) => new(_bounds.Union(other._bounds));
}
