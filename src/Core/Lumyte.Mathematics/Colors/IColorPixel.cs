namespace Lumyte.Mathematics.Colors;

/// <summary>Defines typed component-layout pixel IO without implicit transfer or alpha conversion.</summary>
/// <typeparam name="TSelf">The component-layout pixel type.</typeparam>
public interface IColorPixel<TSelf>
    where TSelf : IColorPixel<TSelf>
{
    /// <summary>Gets a supported pixel's byte count, validating encoding and byte order.</summary>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The byte count of one pixel.</returns>
    static abstract int PixelSize(ColorComponentEncoding encoding, ColorByteOrder byteOrder);

    /// <summary>Reads the first normalized or IEEE pixel; unused input tail is ignored.</summary>
    /// <param name="source">The source pixel bytes.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The decoded component-layout pixel.</returns>
    static abstract TSelf DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder);

    /// <summary>Writes one normalized or IEEE pixel atomically, preserving the unused output tail.</summary>
    /// <param name="destination">The destination pixel bytes.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The number of bytes written.</returns>
    int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder);
}
