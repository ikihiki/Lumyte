using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>One resolved outline and palette entry in a COLRv0 glyph.</summary>
internal readonly record struct ColorGlyphLayer(
    uint GlyphId,
    uint ColorIndex,
    PathGeometry Path);
