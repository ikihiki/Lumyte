namespace Lumyte.Graphics.Text;

/// <summary>An immutable rasterized color-glyph snapshot at its positioned logical bounds.</summary>
/// <param name="Bitmap">The independent sRGB RGBA pixels and logical placement.</param>
public sealed record ColorGlyphGeometry2D(ColorGlyphBitmap2D Bitmap) : TextDrawing2D;
