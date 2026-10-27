namespace Lumyte.Graphics.Text.Parley;

/// <summary>Selects the paragraph's base direction while preserving bidirectional runs.</summary>
public enum ParleyTextDirection2D
{
    /// <summary>Determines direction from the text.</summary>
    Auto,

    /// <summary>Uses a left-to-right paragraph base.</summary>
    LeftToRight,

    /// <summary>Uses a right-to-left paragraph base.</summary>
    RightToLeft,
}
