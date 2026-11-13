namespace Lumyte.Mathematics.Colors;

/// <summary>Byte order within a multibyte component or packed word.</summary>
public enum ColorByteOrder
{
    /// <summary>Least significant byte first.</summary>
    LittleEndian,

    /// <summary>Most significant byte first.</summary>
    BigEndian,
}

/// <summary>CPU pixel storage; no transfer function or alpha association is implied.</summary>
internal enum ColorPixelFormat
{
    /// <summary>Rgb8Unorm storage.</summary>
    Rgb8Unorm,

    /// <summary>Bgr8Unorm storage.</summary>
    Bgr8Unorm,

    /// <summary>Rgba8Unorm storage.</summary>
    Rgba8Unorm,

    /// <summary>Bgra8Unorm storage.</summary>
    Bgra8Unorm,

    /// <summary>Argb8Unorm storage.</summary>
    Argb8Unorm,

    /// <summary>Abgr8Unorm storage.</summary>
    Abgr8Unorm,

    /// <summary>Rgb16Unorm storage.</summary>
    Rgb16Unorm,

    /// <summary>Bgr16Unorm storage.</summary>
    Bgr16Unorm,

    /// <summary>Rgba16Unorm storage.</summary>
    Rgba16Unorm,

    /// <summary>Bgra16Unorm storage.</summary>
    Bgra16Unorm,

    /// <summary>Gray8Unorm storage.</summary>
    Gray8Unorm,

    /// <summary>Gray16Unorm storage.</summary>
    Gray16Unorm,

    /// <summary>GrayAlpha8Unorm storage.</summary>
    GrayAlpha8Unorm,

    /// <summary>GrayAlpha16Unorm storage.</summary>
    GrayAlpha16Unorm,

    /// <summary>Rgb565Unorm storage.</summary>
    Rgb565Unorm,

    /// <summary>Rgba4444Unorm storage.</summary>
    Rgba4444Unorm,

    /// <summary>Rgba5551Unorm storage.</summary>
    Rgba5551Unorm,

    /// <summary>Rgba1010102Unorm storage.</summary>
    Rgba1010102Unorm,

    /// <summary>Bgra1010102Unorm storage.</summary>
    Bgra1010102Unorm,

    /// <summary>Rgba8Snorm storage.</summary>
    Rgba8Snorm,

    /// <summary>Rgba16Snorm storage.</summary>
    Rgba16Snorm,

    /// <summary>Rgb16Float storage.</summary>
    Rgb16Float,

    /// <summary>Rgba16Float storage.</summary>
    Rgba16Float,

    /// <summary>Rgb32Float storage.</summary>
    Rgb32Float,

    /// <summary>Rgba32Float storage.</summary>
    Rgba32Float,

    /// <summary>Rgb64Float storage.</summary>
    Rgb64Float,

    /// <summary>Rgba64Float storage.</summary>
    Rgba64Float,
}
