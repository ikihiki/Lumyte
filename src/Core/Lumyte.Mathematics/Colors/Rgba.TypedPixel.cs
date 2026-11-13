using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <content>Typed RGBA component-encoding pixel operations.</content>
public readonly partial record struct Rgba<T>
    where T : INumber<T>
{
    /// <summary>Gets a supported RGBA pixel size, validating encoding and byte order.</summary>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The byte count of one pixel.</returns>
    public static int PixelSize(ColorComponentEncoding encoding, ColorByteOrder byteOrder) => PixelSize(TypedPixelLayout.Resolve("Rgba", encoding), byteOrder);

    /// <summary>Reads the first normalized or IEEE RGBA pixel without changing transfer or alpha association.</summary>
    /// <param name="source">The source bytes; unused tail is ignored.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The normalized or IEEE RGBA components.</returns>
    public static Rgba<T> DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => DecodePixel(source, TypedPixelLayout.Resolve("Rgba", encoding), byteOrder);

    /// <summary>Writes normalized or IEEE RGBA components atomically; raw code values are not automatically normalized.</summary>
    /// <param name="destination">The output bytes; unused tail is preserved.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The number of bytes written.</returns>
    public int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => WritePixel(destination, TypedPixelLayout.Resolve("Rgba", encoding), byteOrder);
}
