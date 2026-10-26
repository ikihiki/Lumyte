namespace Lumyte.Graphics.Drawing2D;

/// <summary>Extends source-relative texel coordinates independently on each axis.</summary>
public enum ImageTileMode2D
{
    /// <summary>Uses Clamp behavior.</summary>
    Clamp,

    /// <summary>Uses Repeat behavior.</summary>
    Repeat,

    /// <summary>Uses Mirror behavior.</summary>
    Mirror,

    /// <summary>Uses Decal behavior.</summary>
    Decal,
}
