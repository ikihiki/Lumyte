using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Mathematics.Colors;

/// <summary>Batch pixel storage conversions.</summary>
public static partial class Color
{
    /// <summary>Decodes destination.Length pixels; failed conversions retain only the completed prefix.</summary>
    /// <typeparam name="T">The finite component storage type.</typeparam>
    /// <param name="source">Input pixel storage.</param>
    /// <param name="destination">Output pixel storage.</param>
    /// <param name="format">The storage format.</param>
    /// <param name="byteOrder">Component or packed-word byte order.</param>
    public static void DecodePixels<T>(ReadOnlySpan<byte> source, Span<Rgba<T>> destination, ColorPixelFormat format, ColorByteOrder byteOrder)
        where T : INumber<T>
    {
        int size = Rgba<T>.PixelSize(format, byteOrder);
        int required = checked(size * destination.Length);
        if (source.Length < required)
        {
            throw new ArgumentException("The source is shorter than the requested pixels.", nameof(source));
        }

        RejectPixelOverlap(source, (ReadOnlySpan<Rgba<T>>)destination);
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = Rgba<T>.DecodePixel(source[(i * size)..], format, byteOrder);
        }
    }

    /// <summary>Writes source.Length pixels; a failed pixel and all subsequent bytes remain unchanged.</summary>
    /// <typeparam name="T">The finite component storage type.</typeparam>
    /// <param name="source">Input pixel storage.</param>
    /// <param name="destination">Output pixel storage.</param>
    /// <param name="format">The storage format.</param>
    /// <param name="byteOrder">Component or packed-word byte order.</param>
    public static void WritePixels<T>(ReadOnlySpan<Rgba<T>> source, Span<byte> destination, ColorPixelFormat format, ColorByteOrder byteOrder)
        where T : INumber<T>
    {
        int size = Rgba<T>.PixelSize(format, byteOrder);
        int required = checked(size * source.Length);
        if (destination.Length < required)
        {
            throw new ArgumentException("The destination is shorter than the requested pixels.", nameof(destination));
        }

        RejectPixelOverlap((ReadOnlySpan<byte>)destination, source);
        for (int i = 0; i < source.Length; i++)
        {
            source[i].WritePixel(destination[(i * size)..], format, byteOrder);
        }
    }

    private static void RejectPixelOverlap<T>(ReadOnlySpan<byte> bytes, ReadOnlySpan<Rgba<T>> pixels)
        where T : INumber<T>
    {
        if (bytes.IsEmpty || pixels.IsEmpty)
        {
            return;
        }

        // Compare managed byrefs without treating reference-containing values as byte storage.
        ref byte pixelStart = ref Unsafe.As<Rgba<T>, byte>(ref MemoryMarshal.GetReference(pixels));
        ref byte byteStart = ref MemoryMarshal.GetReference(bytes);
        nint offset = Unsafe.ByteOffset(ref pixelStart, ref byteStart);
        nuint pixelBytes = checked((nuint)pixels.Length * (nuint)Unsafe.SizeOf<Rgba<T>>());
        if ((offset >= 0 && (nuint)offset < pixelBytes) || (offset < 0 && (nuint)(-(offset + 1)) < (nuint)(bytes.Length - 1)))
        {
            throw new ArgumentException("Pixel source and destination must not overlap.");
        }
    }
}
