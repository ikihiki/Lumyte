namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies color storage formats without guaranteeing support for every device usage.</summary>
public enum TextureFormat
{
    /// <summary>Four normalized linear eight-bit channels.</summary>
    Rgba8Unorm,

    /// <summary>sRGB color channels and linear eight-bit alpha.</summary>
    Rgba8Srgb,

    /// <summary>Normalized linear BGRA eight-bit channels.</summary>
    Bgra8Unorm,

    /// <summary>sRGB BGRA color and linear eight-bit alpha.</summary>
    Bgra8Srgb,

    /// <summary>Thirty-two-bit floating point depth.</summary>
    Depth32Float,

    /// <summary>At least twenty-four-bit depth and eight-bit stencil; depth storage is backend-specific.</summary>
    Depth24Stencil8,
}
