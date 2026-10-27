namespace Lumyte.Graphics.Text.Parley;

/// <summary>Font-metric-based line decorations emitted as vector geometry after text glyphs.</summary>
[Flags]
public enum ParleyTextDecorations2D
{
    /// <summary>No decorations.</summary>
    None = 0,

    /// <summary>A line below the alphabetic baseline.</summary>
    Underline = 1,

    /// <summary>A line across the glyphs.</summary>
    Strikethrough = 2,

    /// <summary>A line above the glyphs.</summary>
    Overline = 4,
}
