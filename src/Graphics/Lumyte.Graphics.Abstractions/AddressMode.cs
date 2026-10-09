namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies normalized coordinate addressing.</summary>
public enum AddressMode
{
    /// <summary>Clamps coordinates to the texture edge.</summary>
    ClampToEdge,

    /// <summary>Repeats each coordinate interval.</summary>
    Repeat,

    /// <summary>Mirrors alternating coordinate intervals.</summary>
    MirrorRepeat,
}
