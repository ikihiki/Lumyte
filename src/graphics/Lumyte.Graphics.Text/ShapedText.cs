using System.Numerics;

namespace Lumyte.Graphics.Text;

/// <summary>An immutable sequence of glyphs shaped in font units.</summary>
public sealed class ShapedText
{
    internal ShapedText(FontFace owner, ShapedGlyph[] glyphs, Vector2 advance)
    {
        Owner = owner;
        Glyphs = glyphs;
        Advance = advance;
    }

    internal FontFace Owner { get; }

    /// <summary>The shaped glyphs in HarfBuzz visual order.</summary>
    public ReadOnlyMemory<ShapedGlyph> Glyphs { get; }

    /// <summary>The total HarfBuzz pen advance in font units.</summary>
    public Vector2 Advance { get; }

    /// <summary>The total horizontal pen advance in font units.</summary>
    public float XAdvance => Advance.X;

    /// <summary>The total vertical pen advance in font units.</summary>
    public float YAdvance => Advance.Y;
}
