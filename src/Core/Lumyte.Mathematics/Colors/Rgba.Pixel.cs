using System.Numerics;
using Rational = Lumyte.Mathematics.Geometry.GeometryRational;

namespace Lumyte.Mathematics.Colors;

/// <content>Single-pixel storage conversion operations.</content>
public readonly partial record struct Rgba<T>
    where T : INumber<T>
{
    /// <summary>Reads the first pixel, normalizing integer codes without changing transfer or alpha association.</summary>
    /// <param name="source">Input bytes; only the first pixel is read.</param>
    /// <param name="format">The storage format.</param>
    /// <param name="byteOrder">Component or packed-word byte order.</param>
    /// <returns>The normalized or IEEE pixel.</returns>
    internal static Rgba<T> DecodePixel(ReadOnlySpan<byte> source, ColorPixelFormat format, ColorByteOrder byteOrder)
    {
        int size = PixelSize(format, byteOrder);
        if (source.Length < size)
        {
            throw new ArgumentException("The source is shorter than one pixel.", nameof(source));
        }

        PixelLayout(format, out string channels, out int width, out int kind);
        T r = T.Zero;
        T g = T.Zero;
        T b = T.Zero;
        T a = T.One;
        ulong packed = kind == 3 ? ReadWord(source, size, byteOrder) : 0;
        for (int i = 0; i < channels.Length; i++)
        {
            T value;
            if (kind == 3)
            {
                PackedChannel(format, i, out int shift, out int bits);
                ulong maximum = (1UL << bits) - 1;
                value = PixelRatio((long)((packed >> shift) & maximum), (long)maximum);
            }
            else
            {
                ulong code = ReadWord(source[(i * width)..], width, byteOrder);
                value = kind switch
                {
                    0 => PixelRatio((long)code, width == 1 ? 255 : 65535),
                    1 => PixelRatio(Math.Max(width == 1 ? unchecked((sbyte)code) : unchecked((short)code), width == 1 ? -127 : -32767), width == 1 ? 127 : 32767),
                    _ => DecodeFloat(code, width),
                };
            }

            switch (channels[i])
            {
                case 'R': r = value; break;
                case 'G': g = value; break;
                case 'B': b = value; break;
                case 'A': a = value; break;
                case 'Y': r = g = b = value; break;
            }
        }

        return new(r, g, b, a);
    }

    /// <summary>Writes one pixel atomically and returns its byte count.</summary>
    /// <param name="destination">Output bytes; unused tail is preserved.</param>
    /// <param name="format">The storage format.</param>
    /// <param name="byteOrder">Component or packed-word byte order.</param>
    /// <returns>The number of bytes written.</returns>
    internal int WritePixel(Span<byte> destination, ColorPixelFormat format, ColorByteOrder byteOrder)
    {
        int size = PixelSize(format, byteOrder);
        if (destination.Length < size)
        {
            throw new ArgumentException("The destination is shorter than one pixel.", nameof(destination));
        }

        PixelLayout(format, out string channels, out int width, out int kind);
        if (!channels.Contains('A') && A != T.One)
        {
            throw new ArgumentOutOfRangeException(nameof(format), "An alpha-less format requires alpha one.");
        }

        if (channels.Contains('Y') && (R != G || R != B))
        {
            throw new ArgumentOutOfRangeException(nameof(format), "Gray storage requires equal RGB components.");
        }

        Span<byte> temporary = stackalloc byte[32];
        ulong packed = 0;
        for (int i = 0; i < channels.Length; i++)
        {
            T value = channels[i] switch { 'R' or 'Y' => R, 'G' => G, 'B' => B, _ => A };
            if (kind == 3)
            {
                PackedChannel(format, i, out int shift, out int bits);
                packed |= QuantizePixel(value, (1 << bits) - 1, false) << shift;
            }
            else
            {
                ulong code = kind switch
                {
                    0 => QuantizePixel(value, width == 1 ? 255 : 65535, false),
                    1 => QuantizePixel(value, width == 1 ? 127 : 32767, true),
                    _ => EncodeFloat(value, width),
                };
                WriteWord(temporary[(i * width)..], width, code, byteOrder);
            }
        }

        if (kind == 3)
        {
            WriteWord(temporary, size, packed, byteOrder);
        }

        temporary[..size].CopyTo(destination);
        return size;
    }

    internal static int PixelSize(ColorPixelFormat format, ColorByteOrder byteOrder)
    {
        if (byteOrder is not (ColorByteOrder.LittleEndian or ColorByteOrder.BigEndian))
        {
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        }

        PixelLayout(format, out string channels, out int width, out int kind);
        return kind == 3 ? width : channels.Length * width;
    }

    private static void PixelLayout(ColorPixelFormat format, out string channels, out int width, out int kind)
    {
        (channels, width, kind) = format switch
        {
            ColorPixelFormat.Rgb8Unorm => ("RGB", 1, 0),
            ColorPixelFormat.Bgr8Unorm => ("BGR", 1, 0),
            ColorPixelFormat.Rgba8Unorm => ("RGBA", 1, 0),
            ColorPixelFormat.Bgra8Unorm => ("BGRA", 1, 0),
            ColorPixelFormat.Argb8Unorm => ("ARGB", 1, 0),
            ColorPixelFormat.Abgr8Unorm => ("ABGR", 1, 0),
            ColorPixelFormat.Rgb16Unorm => ("RGB", 2, 0),
            ColorPixelFormat.Bgr16Unorm => ("BGR", 2, 0),
            ColorPixelFormat.Rgba16Unorm => ("RGBA", 2, 0),
            ColorPixelFormat.Bgra16Unorm => ("BGRA", 2, 0),
            ColorPixelFormat.Gray8Unorm => ("Y", 1, 0),
            ColorPixelFormat.Gray16Unorm => ("Y", 2, 0),
            ColorPixelFormat.GrayAlpha8Unorm => ("YA", 1, 0),
            ColorPixelFormat.GrayAlpha16Unorm => ("YA", 2, 0),
            ColorPixelFormat.Rgb565Unorm => ("RGB", 2, 3),
            ColorPixelFormat.Rgba4444Unorm or ColorPixelFormat.Rgba5551Unorm => ("RGBA", 2, 3),
            ColorPixelFormat.Rgba1010102Unorm => ("RGBA", 4, 3),
            ColorPixelFormat.Bgra1010102Unorm => ("BGRA", 4, 3),
            ColorPixelFormat.Rgba8Snorm => ("RGBA", 1, 1),
            ColorPixelFormat.Rgba16Snorm => ("RGBA", 2, 1),
            ColorPixelFormat.Rgb16Float => ("RGB", 2, 2),
            ColorPixelFormat.Rgba16Float => ("RGBA", 2, 2),
            ColorPixelFormat.Rgb32Float => ("RGB", 4, 2),
            ColorPixelFormat.Rgba32Float => ("RGBA", 4, 2),
            ColorPixelFormat.Rgb64Float => ("RGB", 8, 2),
            ColorPixelFormat.Rgba64Float => ("RGBA", 8, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static void PackedChannel(ColorPixelFormat format, int index, out int shift, out int bits)
    {
        (shift, bits) = format switch
        {
            ColorPixelFormat.Rgb565Unorm => index switch { 0 => (11, 5), 1 => (5, 6), _ => (0, 5) },
            ColorPixelFormat.Rgba4444Unorm => (12 - (4 * index), 4),
            ColorPixelFormat.Rgba5551Unorm => index == 3 ? (0, 1) : (11 - (5 * index), 5),
            _ => index == 3 ? (30, 2) : (10 * index, 10),
        };
    }

    private static ulong ReadWord(ReadOnlySpan<byte> source, int width, ColorByteOrder order)
    {
        ulong value = 0;
        for (int i = 0; i < width; i++)
        {
            value |= (ulong)source[i] << (8 * (order == ColorByteOrder.LittleEndian ? i : width - 1 - i));
        }

        return value;
    }

    private static void WriteWord(Span<byte> destination, int width, ulong value, ColorByteOrder order)
    {
        for (int i = 0; i < width; i++)
        {
            destination[i] = (byte)(value >> (8 * (order == ColorByteOrder.LittleEndian ? i : width - 1 - i)));
        }
    }

    private static T PixelRatio(long numerator, long denominator)
    {
        // Range failure takes precedence over fractional-result failure for unsigned storage.
        if (numerator < 0 && (typeof(T) == typeof(byte) || typeof(T) == typeof(ushort) || typeof(T) == typeof(uint)
            || typeof(T) == typeof(ulong) || typeof(T) == typeof(nuint) || typeof(T) == typeof(UInt128)))
        {
            throw new OverflowException("A negative component cannot be stored in an unsigned result.");
        }

        return new Rational(numerator, denominator).To<T>();
    }

    private static T DecodeFloat(ulong bits, int width)
    {
        if (width == 2)
        {
            Half value = BitConverter.UInt16BitsToHalf((ushort)bits);
            return PixelFloatValue(value);
        }

        return width == 4 ? PixelFloatValue(BitConverter.UInt32BitsToSingle((uint)bits)) : PixelFloatValue(BitConverter.UInt64BitsToDouble(bits));
    }

    private static T PixelFloatValue<TFloat>(TFloat value)
        where TFloat : IFloatingPointIeee754<TFloat>
    {
        T result = Rational.From(value).To<T>();
        return TFloat.IsZero(value) && TFloat.IsNegative(value) ? T.CreateChecked(value) : result;
    }

    private static ulong QuantizePixel(T value, int maximum, bool signed)
    {
        if (!T.IsFinite(value) || value > T.One || (signed ? Rational.From(value).Numerator < -Rational.From(value).Denominator : value < T.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var rational = Rational.From(value);
        BigInteger magnitude = BigInteger.Abs(rational.Numerator) * maximum;
        var code = BigInteger.DivRem(magnitude, rational.Denominator, out BigInteger remainder);
        int comparison = (2 * remainder).CompareTo(rational.Denominator);
        if (comparison > 0 || (comparison == 0 && !code.IsEven))
        {
            code++;
        }

        long result = (long)code * rational.Numerator.Sign;
        return unchecked((ulong)result);
    }

    private static ulong EncodeFloat(T value, int width)
    {
        if (!T.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var rational = Rational.From(value);

        // Preserve IEEE signed zero when no arithmetic is requested.
        bool negativeZero = T.IsZero(value) && T.IsNegative(value);
        return width switch
        {
            2 => negativeZero ? 0x8000UL : BitConverter.HalfToUInt16Bits(rational.To<Half>()),
            4 => negativeZero ? 0x80000000UL : BitConverter.SingleToUInt32Bits(rational.To<float>()),
            _ => negativeZero ? 0x8000000000000000UL : BitConverter.DoubleToUInt64Bits(rational.To<double>()),
        };
    }
}
