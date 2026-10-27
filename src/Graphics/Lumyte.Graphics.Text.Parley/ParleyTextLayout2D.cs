namespace Lumyte.Graphics.Text.Parley;

/// <summary>An immutable drawing snapshot with positioned glyph and source-cluster information.</summary>
public sealed class ParleyTextLayout2D
{
    internal ParleyTextLayout2D(TextLayout2D layout, IEnumerable<ParleyGlyph2D> glyphs)
    {
        Layout = layout;
        Glyphs = Array.AsReadOnly(glyphs.ToArray());
    }

    /// <summary>Gets the backend-independent drawing snapshot for direct or distance-field rendering.</summary>
    public TextLayout2D Layout { get; }

    /// <summary>Gets the glyphs in visual painter order, preserving logical source cluster offsets.</summary>
    public IReadOnlyList<ParleyGlyph2D> Glyphs { get; }
}
