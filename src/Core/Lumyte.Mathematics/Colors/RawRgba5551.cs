using System.Buffers.Binary;
using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>A raw packed UNORM word with R:15..11, G:10..6, B:5..1, A:0.</summary>
public readonly record struct RawRgba5551 : IPackedColorPixel<RawRgba5551>
{
    /// <summary>Initializes a new instance of the <see cref="RawRgba5551"/> struct from its exact packed bits.</summary>
    /// <param name="packedValue">The raw integer word.</param>
    public RawRgba5551(ushort packedValue)
    {
        PackedValue = packedValue;
    }

    /// <summary>Gets the raw packed integer word.</summary>
    public ushort PackedValue { get; }

    /// <summary>Gets the packed word size in bytes.</summary>
    public static int ByteSize => 2;

    /// <summary>Reads the first raw word without changing its bits.</summary>
    /// <param name="source">Input storage; unused bytes are ignored.</param>
    /// <param name="byteOrder">The packed word byte order.</param>
    /// <returns>The packed pixel.</returns>
    public static RawRgba5551 Read(ReadOnlySpan<byte> source, ColorByteOrder byteOrder)
    {
        ValidateByteOrder(byteOrder);
        if (source.Length < ByteSize)
        {
            throw new ArgumentException("The source is shorter than one packed pixel.", nameof(source));
        }

        return new(byteOrder == ColorByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(source)
            : BinaryPrimitives.ReadUInt16BigEndian(source));
    }

    /// <summary>Writes the raw word atomically, preserving the unused tail.</summary>
    /// <param name="destination">Output storage.</param>
    /// <param name="byteOrder">The packed word byte order.</param>
    /// <returns>The number of bytes written.</returns>
    public int Write(Span<byte> destination, ColorByteOrder byteOrder)
    {
        ValidateByteOrder(byteOrder);
        if (destination.Length < ByteSize)
        {
            throw new ArgumentException("The destination is shorter than one packed pixel.", nameof(destination));
        }

        if (byteOrder == ColorByteOrder.LittleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, PackedValue);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination, PackedValue);
        }

        return ByteSize;
    }

    /// <summary>Quantizes unit sRGB components without transfer or alpha-association conversion.</summary>
    /// <typeparam name="T">The finite numeric component type.</typeparam>
    /// <param name="color">Unit sRGB components; alpha must be one for an alpha-less format.</param>
    /// <returns>The exact nearest-even UNORM packed word.</returns>
    public static RawRgba5551 FromSrgb<T>(SRgba<T> color)
        where T : INumber<T> => FromNormalized(color.Components);

    /// <summary>Quantizes unit linear components without transfer or alpha-association conversion.</summary>
    /// <typeparam name="T">The finite numeric component type.</typeparam>
    /// <param name="color">Unit linear components; alpha must be one for an alpha-less format.</param>
    /// <returns>The exact nearest-even UNORM packed word.</returns>
    public static RawRgba5551 FromLinear<T>(LRgba<T> color)
        where T : INumber<T> => FromNormalized(color.Components);

    /// <summary>Interprets the bits as normalized sRGB components without transfer or alpha-association conversion.</summary>
    /// <typeparam name="T">The result component type; fractional results must be representable.</typeparam>
    /// <returns>Normalized sRGB components, with alpha one for an alpha-less format.</returns>
    public SRgba<T> InterpretSrgb<T>()
        where T : INumber<T> => new(ToNormalized<T>());

    /// <summary>Interprets the bits as normalized linear components without transfer or alpha-association conversion.</summary>
    /// <typeparam name="T">The result component type; fractional results must be representable.</typeparam>
    /// <returns>Normalized linear components, with alpha one for an alpha-less format.</returns>
    public LRgba<T> InterpretLinear<T>()
        where T : INumber<T> => new(ToNormalized<T>());

    /// <summary>Quantizes normalized RGBA using exact nearest-even UNORM conversion.</summary>
    /// <typeparam name="T">The finite numeric component type.</typeparam>
    /// <param name="color">Normalized RGBA; alpha must be one when the format has no alpha.</param>
    /// <returns>The quantized packed pixel.</returns>
    private static RawRgba5551 FromNormalized<T>(Rgba<T> color)
        where T : INumber<T>
    {
        Span<byte> bytes = stackalloc byte[2];
        color.WritePixel(bytes, ColorPixelFormat.Rgba5551Unorm, ColorByteOrder.LittleEndian);
        return Read(bytes, ColorByteOrder.LittleEndian);
    }

    /// <summary>Normalizes each packed channel without changing transfer or alpha association.</summary>
    /// <typeparam name="T">The result component type; fractional results must be representable.</typeparam>
    /// <returns>Normalized RGBA, with alpha one for an alpha-less format.</returns>
    private Rgba<T> ToNormalized<T>()
        where T : INumber<T>
    {
        Span<byte> bytes = stackalloc byte[2];
        Write(bytes, ColorByteOrder.LittleEndian);
        return Rgba<T>.DecodePixel(bytes, ColorPixelFormat.Rgba5551Unorm, ColorByteOrder.LittleEndian);
    }

    private static void ValidateByteOrder(ColorByteOrder byteOrder)
    {
        if (byteOrder is not (ColorByteOrder.LittleEndian or ColorByteOrder.BigEndian))
        {
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        }
    }
}
