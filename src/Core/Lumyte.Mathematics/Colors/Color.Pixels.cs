using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Mathematics.Colors;

/// <summary>Batch pixel storage conversions.</summary>
public static partial class Color
{
    /// <summary>Reads destination.Length pixels; conversion failures preserve the failed pixel and suffix.</summary>
    /// <remarks>Checked sizes, enums, lengths and all overlap (including unused tails) are validated before writing. Preflight failures leave all output unchanged.</remarks>
    /// <typeparam name="TPixel">The concrete component-layout pixel type.</typeparam>
    /// <param name="source">Input storage.</param>
    /// <param name="destination">Output storage; unused tail is preserved.</param>
    /// <param name="encoding">The component numeric encoding.</param>
    /// <param name="byteOrder">The explicit component byte order.</param>
    public static void DecodePixels<TPixel>(ReadOnlySpan<byte> source, Span<TPixel> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder)
        where TPixel : IColorPixel<TPixel>
    {
        int size = TPixel.PixelSize(encoding, byteOrder);
        int required = checked(size * destination.Length);
        if (source.Length < required)
        {
            throw new ArgumentException("The source is shorter than the requested pixels.", nameof(source));
        }

        RejectPixelOverlap(source, (ReadOnlySpan<TPixel>)destination);
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = TPixel.DecodePixel(source.Slice(i * size, size), encoding, byteOrder);
        }
    }

    /// <summary>Writes source.Length pixels; conversion failures preserve the failed pixel and suffix.</summary>
    /// <remarks>Checked sizes, enums, lengths and all overlap (including unused tails) are validated before writing. Preflight failures leave all output unchanged.</remarks>
    /// <typeparam name="TPixel">The concrete component-layout pixel type.</typeparam>
    /// <param name="source">Input storage.</param>
    /// <param name="destination">Output storage; unused tail is preserved.</param>
    /// <param name="encoding">The component numeric encoding.</param>
    /// <param name="byteOrder">The explicit component byte order.</param>
    public static void WritePixels<TPixel>(ReadOnlySpan<TPixel> source, Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder)
        where TPixel : IColorPixel<TPixel>
    {
        int size = TPixel.PixelSize(encoding, byteOrder);
        int required = checked(size * source.Length);
        if (destination.Length < required)
        {
            throw new ArgumentException("The destination is shorter than the requested pixels.", nameof(destination));
        }

        RejectPixelOverlap((ReadOnlySpan<byte>)destination, source);
        for (int i = 0; i < source.Length; i++)
        {
            source[i].WritePixel(destination.Slice(i * size, size), encoding, byteOrder);
        }
    }

    /// <summary>Reads destination.Length pixels; conversion failures preserve the failed pixel and suffix.</summary>
    /// <remarks>Checked sizes, enums, lengths and all overlap (including unused tails) are validated before writing. Preflight failures leave all output unchanged.</remarks>
    /// <typeparam name="TPixel">The concrete packed pixel type.</typeparam>
    /// <param name="source">Input storage.</param>
    /// <param name="destination">Output storage; unused tail is preserved.</param>
    /// <param name="byteOrder">The explicit packed-word byte order.</param>
    public static void ReadPackedPixels<TPixel>(ReadOnlySpan<byte> source, Span<TPixel> destination, ColorByteOrder byteOrder)
        where TPixel : IPackedColorPixel<TPixel>
    {
        ValidatePackedByteOrder(byteOrder);
        int size = TPixel.ByteSize;
        int required = checked(size * destination.Length);
        if (source.Length < required)
        {
            throw new ArgumentException("The source is shorter than the requested pixels.", nameof(source));
        }

        RejectPixelOverlap(source, (ReadOnlySpan<TPixel>)destination);
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = TPixel.Read(source.Slice(i * size, size), byteOrder);
        }
    }

    /// <summary>Writes source.Length pixels; conversion failures preserve the failed pixel and suffix.</summary>
    /// <remarks>Checked sizes, enums, lengths and all overlap (including unused tails) are validated before writing. Preflight failures leave all output unchanged.</remarks>
    /// <typeparam name="TPixel">The concrete packed pixel type.</typeparam>
    /// <param name="source">Input storage.</param>
    /// <param name="destination">Output storage; unused tail is preserved.</param>
    /// <param name="byteOrder">The explicit packed-word byte order.</param>
    public static void WritePackedPixels<TPixel>(ReadOnlySpan<TPixel> source, Span<byte> destination, ColorByteOrder byteOrder)
        where TPixel : IPackedColorPixel<TPixel>
    {
        ValidatePackedByteOrder(byteOrder);
        int size = TPixel.ByteSize;
        int required = checked(size * source.Length);
        if (destination.Length < required)
        {
            throw new ArgumentException("The destination is shorter than the requested pixels.", nameof(destination));
        }

        RejectPixelOverlap((ReadOnlySpan<byte>)destination, source);
        for (int i = 0; i < source.Length; i++)
        {
            source[i].Write(destination.Slice(i * size, size), byteOrder);
        }
    }

    private static void ValidatePackedByteOrder(ColorByteOrder byteOrder)
    {
        if (byteOrder is not (ColorByteOrder.LittleEndian or ColorByteOrder.BigEndian))
        {
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        }
    }

    private static void RejectPixelOverlap<TPixel>(ReadOnlySpan<byte> bytes, ReadOnlySpan<TPixel> pixels)
    {
        if (bytes.IsEmpty || pixels.IsEmpty)
        {
            return;
        }

        // Compare managed byrefs without reinterpreting reference-containing values as byte storage.
        ref byte pixelStart = ref Unsafe.As<TPixel, byte>(ref MemoryMarshal.GetReference(pixels));
        ref byte byteStart = ref MemoryMarshal.GetReference(bytes);
        nint offset = Unsafe.ByteOffset(ref pixelStart, ref byteStart);
        nuint pixelBytes = checked((nuint)pixels.Length * (nuint)Unsafe.SizeOf<TPixel>());
        if ((offset >= 0 && (nuint)offset < pixelBytes) || (offset < 0 && (nuint)(-(offset + 1)) < (nuint)(bytes.Length - 1)))
        {
            throw new ArgumentException("Pixel source and destination must not overlap.");
        }
    }
}
