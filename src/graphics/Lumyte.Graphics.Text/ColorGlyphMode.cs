namespace Lumyte.Graphics.Text;

/// <summary>Controls whether embedded vector and bitmap color glyphs are used.</summary>
public enum ColorGlyphMode
{
    /// <summary>Use COLR/CPAL or CBDT/sbix color data when available, then fall back to the outline.</summary>
    Auto,

    /// <summary>Ignore embedded color information and draw every glyph with the caller's brush.</summary>
    Monochrome,
}
