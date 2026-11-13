namespace Lumyte.Mathematics.Colors;

/// <summary>A raw packed integer pixel with an explicit serialized byte order.</summary>
/// <typeparam name="TSelf">The concrete packed pixel type.</typeparam>
public interface IPackedColorPixel<TSelf>
    where TSelf : IPackedColorPixel<TSelf>
{
    /// <summary>Gets the number of bytes in one packed word.</summary>
    static abstract int ByteSize { get; }

    /// <summary>Reads the first packed word without normalizing or changing any bits.</summary>
    /// <param name="source">Input storage containing at least one word.</param>
    /// <param name="byteOrder">The packed word byte order.</param>
    /// <returns>The raw packed pixel.</returns>
    static abstract TSelf Read(ReadOnlySpan<byte> source, ColorByteOrder byteOrder);

    /// <summary>Writes one raw word, preserving the unused destination tail.</summary>
    /// <param name="destination">Output storage containing at least one word.</param>
    /// <param name="byteOrder">The packed word byte order.</param>
    /// <returns>The number of bytes written.</returns>
    int Write(Span<byte> destination, ColorByteOrder byteOrder);
}
