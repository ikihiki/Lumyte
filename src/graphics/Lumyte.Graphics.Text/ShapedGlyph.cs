namespace Lumyte.Graphics.Text;

/// <summary>
/// A HarfBuzz-shaped glyph expressed in font units.
/// </summary>
/// <param name="GlyphId">The glyph index in the font face.</param>
/// <param name="Cluster">The UTF-16 code-unit index in the source text.</param>
/// <param name="XAdvance">The horizontal pen advance in font units.</param>
/// <param name="YAdvance">The vertical pen advance in font units, using HarfBuzz's y-up convention.</param>
/// <param name="XOffset">The horizontal glyph offset in font units.</param>
/// <param name="YOffset">The vertical glyph offset in font units, using HarfBuzz's y-up convention.</param>
public readonly record struct ShapedGlyph(
    uint GlyphId,
    int Cluster,
    float XAdvance,
    float YAdvance,
    float XOffset,
    float YOffset);
