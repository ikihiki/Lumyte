namespace Lumyte.Graphics;

/// <summary>
/// Specifies the initial supported RGBA8 image formats.
/// </summary>
public enum TextureFormat
{
    /// <summary>
    /// Stores four normalized linear 8-bit channels.
    /// </summary>
    Rgba8Unorm,

    /// <summary>
    /// Stores sRGB color channels and a linear 8-bit alpha channel.
    /// </summary>
    Rgba8Srgb,
}
