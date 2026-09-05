namespace Lumyte.Graphics.Text;

/// <summary>A bottom-to-top sequence of COLRv0 glyph layers.</summary>
internal sealed class ColorGlyph
{
    internal ColorGlyph(ColorGlyphLayer[] layers) => Layers = layers;

    internal IReadOnlyList<ColorGlyphLayer> Layers { get; }
}
