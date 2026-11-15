namespace Lumyte.Mathematics.Colors;

internal static class TypedPixelLayout
{
    internal static ColorPixelFormat Resolve(string layout, ColorComponentEncoding encoding)
    {
        if (encoding is < ColorComponentEncoding.Unorm8 or > ColorComponentEncoding.Float64)
        {
            throw new ArgumentOutOfRangeException(nameof(encoding));
        }

        return (layout, encoding) switch
        {
            ("Gray", ColorComponentEncoding.Unorm8) => ColorPixelFormat.Gray8Unorm,
            ("Gray", ColorComponentEncoding.Unorm16) => ColorPixelFormat.Gray16Unorm,
            ("GrayAlpha", ColorComponentEncoding.Unorm8) => ColorPixelFormat.GrayAlpha8Unorm,
            ("GrayAlpha", ColorComponentEncoding.Unorm16) => ColorPixelFormat.GrayAlpha16Unorm,
            ("Rgb", ColorComponentEncoding.Unorm8) => ColorPixelFormat.Rgb8Unorm,
            ("Rgb", ColorComponentEncoding.Unorm16) => ColorPixelFormat.Rgb16Unorm,
            ("Rgb", ColorComponentEncoding.Float16) => ColorPixelFormat.Rgb16Float,
            ("Rgb", ColorComponentEncoding.Float32) => ColorPixelFormat.Rgb32Float,
            ("Rgb", ColorComponentEncoding.Float64) => ColorPixelFormat.Rgb64Float,
            ("Bgr", ColorComponentEncoding.Unorm8) => ColorPixelFormat.Bgr8Unorm,
            ("Bgr", ColorComponentEncoding.Unorm16) => ColorPixelFormat.Bgr16Unorm,
            ("Rgba", ColorComponentEncoding.Unorm8) => ColorPixelFormat.Rgba8Unorm,
            ("Rgba", ColorComponentEncoding.Unorm16) => ColorPixelFormat.Rgba16Unorm,
            ("Rgba", ColorComponentEncoding.Snorm8) => ColorPixelFormat.Rgba8Snorm,
            ("Rgba", ColorComponentEncoding.Snorm16) => ColorPixelFormat.Rgba16Snorm,
            ("Rgba", ColorComponentEncoding.Float16) => ColorPixelFormat.Rgba16Float,
            ("Rgba", ColorComponentEncoding.Float32) => ColorPixelFormat.Rgba32Float,
            ("Rgba", ColorComponentEncoding.Float64) => ColorPixelFormat.Rgba64Float,
            ("Bgra", ColorComponentEncoding.Unorm8) => ColorPixelFormat.Bgra8Unorm,
            ("Bgra", ColorComponentEncoding.Unorm16) => ColorPixelFormat.Bgra16Unorm,
            ("Argb", ColorComponentEncoding.Unorm8) => ColorPixelFormat.Argb8Unorm,
            ("Abgr", ColorComponentEncoding.Unorm8) => ColorPixelFormat.Abgr8Unorm,
            _ => throw new ArgumentException("The component layout does not support this encoding.", nameof(encoding)),
        };
    }

    internal static void ValidateByteOrder(ColorByteOrder byteOrder)
    {
        if (byteOrder is not (ColorByteOrder.LittleEndian or ColorByteOrder.BigEndian))
        {
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        }
    }
}
