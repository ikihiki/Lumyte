using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text;

/// <summary>An immutable CPU raster snapshot of a color glyph, using straight-alpha sRGB RGBA bytes.</summary>
public sealed class ColorGlyphBitmap2D
{
    private readonly byte[] _pixels;

    internal ColorGlyphBitmap2D(int width, int height, Rect2D bounds, byte[] pixels)
    {
        Width = width;
        Height = height;
        Bounds = bounds;
        _pixels = pixels;
    }

    /// <summary>Gets the raster width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the raster height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the glyph rectangle in logical coordinates, relative to its baseline or positioned layout.</summary>
    public Rect2D Bounds { get; }

    /// <summary>Gets tightly packed straight-alpha sRGB RGBA bytes, independent of native resources.</summary>
    public ReadOnlyMemory<byte> Pixels => _pixels;

    internal ColorGlyphBitmap2D Translate(float x, float y) => new(Width, Height, Bounds with { X = Bounds.X + x, Y = Bounds.Y + y }, _pixels);
}
