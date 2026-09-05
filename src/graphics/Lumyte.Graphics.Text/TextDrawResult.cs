using System.Numerics;

namespace Lumyte.Graphics.Text;

/// <summary>Describes the placement and selected route of one recorded text run.</summary>
public readonly record struct TextDrawResult(
    Vector2 Advance,
    int GlyphCount,
    TextRenderingMode RenderingMode,
    float EffectiveFontSize,
    int FallbackGlyphCount,
    /// <summary>The number of glyphs drawn from any embedded color representation.</summary>
    int ColorGlyphCount = 0,
    /// <summary>The subset of color glyphs drawn from embedded PNG bitmaps.</summary>
    int BitmapGlyphCount = 0);
