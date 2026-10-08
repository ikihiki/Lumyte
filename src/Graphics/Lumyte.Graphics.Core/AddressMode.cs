namespace Lumyte.Graphics;

/// <summary>
/// Specifies sampling outside the normalized texture-coordinate range.
/// </summary>
public enum AddressMode
{
    /// <summary>
    /// Clamps coordinates to the image edge.
    /// </summary>
    ClampToEdge,

    /// <summary>
    /// Repeats the image in each coordinate interval.
    /// </summary>
    Repeat,

    /// <summary>
    /// Alternates normal and mirrored image intervals.
    /// </summary>
    MirrorRepeat,
}
