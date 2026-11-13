using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Independent byte oracles for every format, endian and failure boundary.</summary>
public sealed class ColorPixelTests
{
    private enum FormatCase
    {
        Rgb8Unorm,
        Bgr8Unorm,
        Rgba8Unorm,
        Bgra8Unorm,
        Argb8Unorm,
        Abgr8Unorm,
        Rgb16Unorm,
        Bgr16Unorm,
        Rgba16Unorm,
        Bgra16Unorm,
        Gray8Unorm,
        Gray16Unorm,
        GrayAlpha8Unorm,
        GrayAlpha16Unorm,
        Rgb565Unorm,
        Rgba4444Unorm,
        Rgba5551Unorm,
        Rgba1010102Unorm,
        Bgra1010102Unorm,
        Rgba8Snorm,
        Rgba16Snorm,
        Rgb16Float,
        Rgba16Float,
        Rgb32Float,
        Rgba32Float,
        Rgb64Float,
        Rgba64Float,
    }

    /// <summary>All 27 storage layouts are checked against independently assembled channel words.</summary>
    [Fact]
    public void AllFormatsAndEndianMatchKnownBytes()
    {
        Assert.Equal(27, Enum.GetValues<FormatCase>().Length);
        foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
        {
            foreach (FormatCase format in Enum.GetValues<FormatCase>())
            {
                (byte[] bytes, Rgba<double> expected) = Oracle(format, order);
                byte[] source = [.. bytes, 0xED, 0xFE];
                Assert.Equal(expected, Decode(source, format, order));
                byte[] output = Enumerable.Repeat((byte)0xCD, bytes.Length + 3).ToArray();
                Assert.Equal(bytes.Length, Write(expected, output, format, order));
                Assert.Equal(bytes, output[..bytes.Length]);
                Assert.All(output[bytes.Length..], value => Assert.Equal(0xCD, value));
                CheckBatch(format, order, bytes, expected);
            }
        }
    }

    /// <summary>SNORM minimum decodes to -1, whose canonical encoding is the symmetric minimum.</summary>
    /// <param name="format">The signed component format.</param>
    /// <param name="width">The component byte width.</param>
    [Theory]
    [InlineData(ColorComponentEncoding.Snorm8, 1)]
    [InlineData(ColorComponentEncoding.Snorm16, 2)]
    public void SnormMinimumAndUnsignedFailure(ColorComponentEncoding format, int width)
    {
        foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
        {
            byte[] input = Words([1UL << ((width * 8) - 1), 0, 0, 0], width, order);
            var color = Rgba<double>.DecodePixel(input, format, order);
            Assert.Equal(-1, color.R);
            byte[] output = new byte[input.Length];
            color.WritePixel(output, format, order);
            ulong canonical = width == 1 ? 129UL : 32769UL;
            Assert.Equal(Words([canonical, 0, 0, 0], width, order), output);
            Assert.Throws<OverflowException>(() => Rgba<uint>.DecodePixel(input, format, order));
            Assert.Throws<ArithmeticException>(() => Rgba<int>.DecodePixel(Words([1, 0, 0, 0], width, order), format, order));
        }
    }

    /// <summary>IEEE formats retain negative HDR and subnormals; overflow and nonfinite input fail.</summary>
    [Fact]
    public void IeeePrecisionAndExceptionalValues()
    {
        foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
        {
            foreach ((ColorComponentEncoding format, int width) in new[] { (ColorComponentEncoding.Float16, 2), (ColorComponentEncoding.Float32, 4), (ColorComponentEncoding.Float64, 8) })
            {
                double epsilon = width == 2 ? (double)Half.Epsilon : width == 4 ? float.Epsilon : double.Epsilon;
                var color = new Rgba<double>(-2, 16, epsilon, -0d);
                byte[] output = new byte[width * 4];
                color.WritePixel(output, format, order);
                Assert.Equal(color, Rgba<double>.DecodePixel(output, format, order));
                ulong infinity = width == 2 ? 0x7C00UL : width == 4 ? 0x7F800000UL : 0x7FF0000000000000UL;
                Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.DecodePixel(Words([0, 0, infinity, 0], width, order), format, order));
                Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.DecodePixel(Words([0, 0, infinity | 1, 0], width, order), format, order));
                byte[] before = (byte[])output.Clone();
                var huge = new Rgba<BigInteger>(0, 1, BigInteger.One << 20000, 1);
                Assert.Throws<OverflowException>(() => huge.WritePixel(output, format, order));
                Assert.Equal(before, output);
            }
        }

        byte[] rounded = new byte[8];
        new Rgba<decimal>(1.00048828125m, 1.00146484375m, 0, 1).WritePixel(rounded, ColorComponentEncoding.Float16, ColorByteOrder.LittleEndian);
        Assert.Equal(Words([0x3C00, 0x3C02, 0, 0x3C00], 2, ColorByteOrder.LittleEndian), rounded);
        Assert.Throws<ArithmeticException>(() => Rgba<int>.DecodePixel(Words([0x3E00, 0, 0, 0], 2, ColorByteOrder.LittleEndian), ColorComponentEncoding.Float16, ColorByteOrder.LittleEndian));
    }

    /// <summary>Single-pixel validation is atomic for every format and endian.</summary>
    [Fact]
    public void SinglePixelFailuresAreAtomic()
    {
        foreach (FormatCase format in Enum.GetValues<FormatCase>())
        {
            foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
            {
                (byte[] bytes, Rgba<double> color) = Oracle(format, order);
                byte[] output = Enumerable.Repeat((byte)0xAB, bytes.Length + 1).ToArray();
                byte[] before = (byte[])output.Clone();
                Assert.Throws<ArgumentException>(() => Write(color, output.AsSpan(0, bytes.Length - 1), format, order));
                Assert.Equal(before, output);
                Assert.Throws<ArgumentException>(() => Decode(bytes.AsSpan(1), format, order));
                Assert.Throws<ArgumentOutOfRangeException>(() => Write(color, output, format, (ColorByteOrder)99));
                Assert.Throws<ArgumentOutOfRangeException>(() => color.WritePixel(output, (ColorComponentEncoding)99, order));
                Assert.Equal(before, output);
                if (format.ToString().StartsWith("Gray", StringComparison.Ordinal))
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => Write(new Rgba<double>(0, 1, 0, 1), output, format, order));
                    Assert.Equal(before, output);
                }

                bool noAlpha = !format.ToString().Contains("Rgba", StringComparison.Ordinal) && !format.ToString().Contains("Bgra", StringComparison.Ordinal)
                    && !format.ToString().Contains("Argb", StringComparison.Ordinal) && !format.ToString().Contains("Abgr", StringComparison.Ordinal)
                    && !format.ToString().Contains("Alpha", StringComparison.Ordinal);
                if (noAlpha)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => Write(color.WithAlpha(0), output, format, order));
                    Assert.Equal(before, output);
                }
                else if (!format.ToString().Contains("Float", StringComparison.Ordinal))
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => Write(color.WithAlpha(2), output, format, order));
                    Assert.Equal(before, output);
                }
            }
        }
    }

    /// <summary>Batch conversion failure only commits completed pixels, never the failing pixel or tail.</summary>
    [Fact]
    public void BatchFailurePreservesSuffixAndPreflightPreservesEverything()
    {
        var good = new Rgba<double>(1, 0, 0, 1);
        Rgba<double>[] pixels = [good, new(1, 0, 0, 2), good];
        byte[] bytes = Enumerable.Repeat((byte)0xAB, 15).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.WritePixels<Rgba<double>>(pixels, bytes, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bytes[..4]);
        Assert.All(bytes[4..], value => Assert.Equal(0xAB, value));
        byte[] before = (byte[])bytes.Clone();
        Assert.Throws<ArgumentException>(() => Color.WritePixels<Rgba<double>>(pixels, bytes.AsSpan(0, 11), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Equal(before, bytes);
        Rgba<int>[] destination = [new(7, 7, 7, 7), new(7, 7, 7, 7), new(7, 7, 7, 7)];
        byte[] codes = [255, 0, 0, 255, 255, 128, 0, 255, 0, 0, 0, 255];
        Assert.Throws<ArithmeticException>(() => Color.DecodePixels<Rgba<int>>(codes, destination, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Equal(new Rgba<int>(1, 0, 0, 1), destination[0]);
        Assert.Equal(new Rgba<int>(7, 7, 7, 7), destination[1]);
        Assert.Equal(destination[1], destination[2]);
        var saved = (Rgba<int>[])destination.Clone();
        Assert.Throws<ArgumentException>(() => Color.DecodePixels<Rgba<int>>(codes.AsSpan(1), destination, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.DecodePixels<Rgba<int>>(codes, destination, (ColorComponentEncoding)99, ColorByteOrder.LittleEndian));
        Assert.Equal(saved, destination);
        Color.DecodePixels<Rgba<int>>([], [], ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Color.WritePixels<Rgba<int>>([], [], ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.DecodePixels<Rgba<int>>([], [], (ColorComponentEncoding)99, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.WritePixels<Rgba<int>>([], [], ColorComponentEncoding.Unorm8, (ColorByteOrder)99));
    }

    /// <summary>Same-start and shifted overlaps, including unused byte tails, fail before any writes.</summary>
    [Fact]
    public void BatchRejectsAllOverlaps()
    {
        foreach (int offset in new[] { 0, 1, 15, 16, 31 })
        {
            byte[] storage = Enumerable.Repeat((byte)0xAB, 96).ToArray();
            byte[] before = (byte[])storage.Clone();
            Assert.Throws<ArgumentException>(() => Color.DecodePixels<Rgba<int>>(storage.AsSpan(offset, 40), MemoryMarshal.Cast<byte, Rgba<int>>(storage.AsSpan(16, 32)), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
            Assert.Equal(before, storage);
            Assert.Throws<ArgumentException>(() => Color.WritePixels<Rgba<int>>(MemoryMarshal.Cast<byte, Rgba<int>>(storage.AsSpan(16, 32)), storage.AsSpan(offset, 40), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
            Assert.Equal(before, storage);
        }

        byte[] adjacent = new byte[48];
        Color.DecodePixels<Rgba<int>>(adjacent.AsSpan(0, 4), MemoryMarshal.Cast<byte, Rgba<int>>(adjacent.AsSpan(4, 16)), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Color.WritePixels<Rgba<int>>(MemoryMarshal.Cast<byte, Rgba<int>>(adjacent.AsSpan(4, 16)), adjacent.AsSpan(20, 4), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
    }

    /// <summary>UNORM ties use exact mathematical values, including decimal midpoint neighbors.</summary>
    [Fact]
    public void QuantizationTiesAndIntegralDecode()
    {
        byte[] output = new byte[4];
        new Rgba<decimal>(0.1m, 0.3m, 0.5m, 0.7m).WritePixel(output, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Assert.Equal(new byte[] { 26, 76, 128, 178 }, output);
        Assert.Throws<ArithmeticException>(() => Rgba<int>.DecodePixel(new byte[] { 1, 0, 0, 255 }, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Equal(new Rgba<BigInteger>(1, 0, 0, 1), Rgba<BigInteger>.DecodePixel(new byte[] { 255, 0, 0, 255 }, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
    }

    /// <summary>Checked sizes fail before storage access, and reference-containing BigInteger is supported.</summary>
    [Fact]
    public void CheckedSizesAndReferenceContainingStorage()
    {
        Assert.Throws<OverflowException>(() => Color.DecodePixels<Rgba<double>>([], OversizedPixels(), ColorComponentEncoding.Float64, ColorByteOrder.LittleEndian));
        Assert.Throws<OverflowException>(() => Color.WritePixels<Rgba<double>>(OversizedPixels(), [], ColorComponentEncoding.Float64, ColorByteOrder.LittleEndian));
        Rgba<BigInteger>[] integers = [new(1, 0, 1, 1), new(0, 1, 0, 1)];
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<Rgba<BigInteger>>());
        byte[] bytes = new byte[8];
        Color.WritePixels<Rgba<BigInteger>>(integers, bytes, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        var output = new Rgba<BigInteger>[2];
        Color.DecodePixels<Rgba<BigInteger>>(bytes, output, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Assert.Equal(integers, output);
    }

    private static Rgba<double> Decode(ReadOnlySpan<byte> source, FormatCase format, ColorByteOrder order) => format switch
    {
        FormatCase.Rgb8Unorm => Rgb<double>.DecodePixel(source, ColorComponentEncoding.Unorm8, order).ToRgba(1),
        FormatCase.Bgr8Unorm => Bgr<double>.DecodePixel(source, ColorComponentEncoding.Unorm8, order).ToRgba(1),
        FormatCase.Rgba8Unorm => Rgba<double>.DecodePixel(source, ColorComponentEncoding.Unorm8, order),
        FormatCase.Bgra8Unorm => Bgra<double>.DecodePixel(source, ColorComponentEncoding.Unorm8, order).ToRgba(),
        FormatCase.Argb8Unorm => Argb<double>.DecodePixel(source, ColorComponentEncoding.Unorm8, order).ToRgba(),
        FormatCase.Abgr8Unorm => Abgr<double>.DecodePixel(source, ColorComponentEncoding.Unorm8, order).ToRgba(),
        FormatCase.Rgb16Unorm => Rgb<double>.DecodePixel(source, ColorComponentEncoding.Unorm16, order).ToRgba(1),
        FormatCase.Bgr16Unorm => Bgr<double>.DecodePixel(source, ColorComponentEncoding.Unorm16, order).ToRgba(1),
        FormatCase.Rgba16Unorm => Rgba<double>.DecodePixel(source, ColorComponentEncoding.Unorm16, order),
        FormatCase.Bgra16Unorm => Bgra<double>.DecodePixel(source, ColorComponentEncoding.Unorm16, order).ToRgba(),
        FormatCase.Gray8Unorm => Gray<double>.DecodePixel(source, ColorComponentEncoding.Unorm8, order).ToRgba(1),
        FormatCase.Gray16Unorm => Gray<double>.DecodePixel(source, ColorComponentEncoding.Unorm16, order).ToRgba(1),
        FormatCase.GrayAlpha8Unorm => GrayAlpha<double>.DecodePixel(source, ColorComponentEncoding.Unorm8, order).ToRgba(),
        FormatCase.GrayAlpha16Unorm => GrayAlpha<double>.DecodePixel(source, ColorComponentEncoding.Unorm16, order).ToRgba(),
        FormatCase.Rgb565Unorm => Rgb565.Read(source, order).ToNormalized<double>(),
        FormatCase.Rgba4444Unorm => Rgba4444.Read(source, order).ToNormalized<double>(),
        FormatCase.Rgba5551Unorm => Rgba5551.Read(source, order).ToNormalized<double>(),
        FormatCase.Rgba1010102Unorm => Rgba1010102.Read(source, order).ToNormalized<double>(),
        FormatCase.Bgra1010102Unorm => Bgra1010102.Read(source, order).ToNormalized<double>(),
        FormatCase.Rgba8Snorm => Rgba<double>.DecodePixel(source, ColorComponentEncoding.Snorm8, order),
        FormatCase.Rgba16Snorm => Rgba<double>.DecodePixel(source, ColorComponentEncoding.Snorm16, order),
        FormatCase.Rgb16Float => Rgb<double>.DecodePixel(source, ColorComponentEncoding.Float16, order).ToRgba(1),
        FormatCase.Rgba16Float => Rgba<double>.DecodePixel(source, ColorComponentEncoding.Float16, order),
        FormatCase.Rgb32Float => Rgb<double>.DecodePixel(source, ColorComponentEncoding.Float32, order).ToRgba(1),
        FormatCase.Rgba32Float => Rgba<double>.DecodePixel(source, ColorComponentEncoding.Float32, order),
        FormatCase.Rgb64Float => Rgb<double>.DecodePixel(source, ColorComponentEncoding.Float64, order).ToRgba(1),
        FormatCase.Rgba64Float => Rgba<double>.DecodePixel(source, ColorComponentEncoding.Float64, order),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static int Write(Rgba<double> color, Span<byte> destination, FormatCase format, ColorByteOrder order) => format switch
    {
        FormatCase.Rgb8Unorm => Rgb<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm8, order),
        FormatCase.Bgr8Unorm => Bgr<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm8, order),
        FormatCase.Rgba8Unorm => color.WritePixel(destination, ColorComponentEncoding.Unorm8, order),
        FormatCase.Bgra8Unorm => Bgra<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm8, order),
        FormatCase.Argb8Unorm => Argb<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm8, order),
        FormatCase.Abgr8Unorm => Abgr<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm8, order),
        FormatCase.Rgb16Unorm => Rgb<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm16, order),
        FormatCase.Bgr16Unorm => Bgr<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm16, order),
        FormatCase.Rgba16Unorm => color.WritePixel(destination, ColorComponentEncoding.Unorm16, order),
        FormatCase.Bgra16Unorm => Bgra<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm16, order),
        FormatCase.Gray8Unorm => Gray<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm8, order),
        FormatCase.Gray16Unorm => Gray<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm16, order),
        FormatCase.GrayAlpha8Unorm => GrayAlpha<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm8, order),
        FormatCase.GrayAlpha16Unorm => GrayAlpha<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Unorm16, order),
        FormatCase.Rgb565Unorm => Rgb565.FromNormalized(color).Write(destination, order),
        FormatCase.Rgba4444Unorm => Rgba4444.FromNormalized(color).Write(destination, order),
        FormatCase.Rgba5551Unorm => Rgba5551.FromNormalized(color).Write(destination, order),
        FormatCase.Rgba1010102Unorm => Rgba1010102.FromNormalized(color).Write(destination, order),
        FormatCase.Bgra1010102Unorm => Bgra1010102.FromNormalized(color).Write(destination, order),
        FormatCase.Rgba8Snorm => color.WritePixel(destination, ColorComponentEncoding.Snorm8, order),
        FormatCase.Rgba16Snorm => color.WritePixel(destination, ColorComponentEncoding.Snorm16, order),
        FormatCase.Rgb16Float => Rgb<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Float16, order),
        FormatCase.Rgba16Float => color.WritePixel(destination, ColorComponentEncoding.Float16, order),
        FormatCase.Rgb32Float => Rgb<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Float32, order),
        FormatCase.Rgba32Float => color.WritePixel(destination, ColorComponentEncoding.Float32, order),
        FormatCase.Rgb64Float => Rgb<double>.FromRgba(color).WritePixel(destination, ColorComponentEncoding.Float64, order),
        FormatCase.Rgba64Float => color.WritePixel(destination, ColorComponentEncoding.Float64, order),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static void CheckBatch(FormatCase format, ColorByteOrder order, byte[] bytes, Rgba<double> expected)
    {
        switch (format)
        {
            case FormatCase.Rgb8Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm8, Rgb<double>.FromRgba(expected));
                break;
            case FormatCase.Bgr8Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm8, Bgr<double>.FromRgba(expected));
                break;
            case FormatCase.Rgba8Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm8, expected);
                break;
            case FormatCase.Bgra8Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm8, Bgra<double>.FromRgba(expected));
                break;
            case FormatCase.Argb8Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm8, Argb<double>.FromRgba(expected));
                break;
            case FormatCase.Abgr8Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm8, Abgr<double>.FromRgba(expected));
                break;
            case FormatCase.Rgb16Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm16, Rgb<double>.FromRgba(expected));
                break;
            case FormatCase.Bgr16Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm16, Bgr<double>.FromRgba(expected));
                break;
            case FormatCase.Rgba16Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm16, expected);
                break;
            case FormatCase.Bgra16Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm16, Bgra<double>.FromRgba(expected));
                break;
            case FormatCase.Gray8Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm8, Gray<double>.FromRgba(expected));
                break;
            case FormatCase.Gray16Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm16, Gray<double>.FromRgba(expected));
                break;
            case FormatCase.GrayAlpha8Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm8, GrayAlpha<double>.FromRgba(expected));
                break;
            case FormatCase.GrayAlpha16Unorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Unorm16, GrayAlpha<double>.FromRgba(expected));
                break;
            case FormatCase.Rgb565Unorm:
                CheckPackedBatch(bytes, order, Rgb565.FromNormalized(expected));
                break;
            case FormatCase.Rgba4444Unorm:
                CheckPackedBatch(bytes, order, Rgba4444.FromNormalized(expected));
                break;
            case FormatCase.Rgba5551Unorm:
                CheckPackedBatch(bytes, order, Rgba5551.FromNormalized(expected));
                break;
            case FormatCase.Rgba1010102Unorm:
                CheckPackedBatch(bytes, order, Rgba1010102.FromNormalized(expected));
                break;
            case FormatCase.Bgra1010102Unorm:
                CheckPackedBatch(bytes, order, Bgra1010102.FromNormalized(expected));
                break;
            case FormatCase.Rgba8Snorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Snorm8, expected);
                break;
            case FormatCase.Rgba16Snorm:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Snorm16, expected);
                break;
            case FormatCase.Rgb16Float:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Float16, Rgb<double>.FromRgba(expected));
                break;
            case FormatCase.Rgba16Float:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Float16, expected);
                break;
            case FormatCase.Rgb32Float:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Float32, Rgb<double>.FromRgba(expected));
                break;
            case FormatCase.Rgba32Float:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Float32, expected);
                break;
            case FormatCase.Rgb64Float:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Float64, Rgb<double>.FromRgba(expected));
                break;
            case FormatCase.Rgba64Float:
                CheckTypedBatch(bytes, order, ColorComponentEncoding.Float64, expected);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void CheckTypedBatch<TPixel>(byte[] bytes, ColorByteOrder order, ColorComponentEncoding encoding, TPixel expected)
        where TPixel : IColorPixel<TPixel>
    {
        var decoded = new TPixel[3];
        Color.DecodePixels<TPixel>([.. bytes, .. bytes, .. bytes, 0xEF], decoded, encoding, order);
        Assert.All(decoded, value => Assert.Equal(expected, value));
        byte[] output = Enumerable.Repeat((byte)0xCD, (bytes.Length * 3) + 1).ToArray();
        Color.WritePixels<TPixel>(decoded, output, encoding, order);
        Assert.Equal(bytes.Concat(bytes).Concat(bytes), output[..^1]);
        Assert.Equal(0xCD, output[^1]);
    }

    private static void CheckPackedBatch<TPixel>(byte[] bytes, ColorByteOrder order, TPixel expected)
        where TPixel : IPackedColorPixel<TPixel>
    {
        var decoded = new TPixel[3];
        Color.ReadPackedPixels<TPixel>([.. bytes, .. bytes, .. bytes, 0xEF], decoded, order);
        Assert.All(decoded, value => Assert.Equal(expected, value));
        byte[] output = Enumerable.Repeat((byte)0xCD, (bytes.Length * 3) + 1).ToArray();
        Color.WritePackedPixels<TPixel>(decoded, output, order);
        Assert.Equal(bytes.Concat(bytes).Concat(bytes), output[..^1]);
        Assert.Equal(0xCD, output[^1]);
    }

    private static Span<Rgba<double>> OversizedPixels() => MemoryMarshal.CreateSpan(ref Unsafe.NullRef<Rgba<double>>(), (int.MaxValue / 32) + 1);

    private static (byte[] Bytes, Rgba<double> Color) Oracle(FormatCase format, ColorByteOrder order)
    {
        string name = format.ToString();
        if (format is FormatCase.Rgb565Unorm)
        {
            return (Words([(17UL << 11) | (42UL << 5) | 3], 2, order), new(17d / 31, 42d / 63, 3d / 31, 1));
        }

        if (format is FormatCase.Rgba4444Unorm)
        {
            return (Words([0x1A3E], 2, order), new(1d / 15, 10d / 15, 3d / 15, 14d / 15));
        }

        if (format is FormatCase.Rgba5551Unorm)
        {
            return (Words([(17UL << 11) | (10UL << 6) | (3UL << 1) | 1], 2, order), new(17d / 31, 10d / 31, 3d / 31, 1));
        }

        if (format is FormatCase.Rgba1010102Unorm or FormatCase.Bgra1010102Unorm)
        {
            bool bgra = format == FormatCase.Bgra1010102Unorm;
            return (Words([17UL | (666UL << 10) | (333UL << 20) | (2UL << 30)], 4, order), new((bgra ? 333d : 17d) / 1023, 666d / 1023, (bgra ? 17d : 333d) / 1023, 2d / 3));
        }

        int width = name.Contains("64", StringComparison.Ordinal) ? 8 : name.Contains("32", StringComparison.Ordinal) ? 4 : name.Contains("16", StringComparison.Ordinal) ? 2 : 1;
        string channels = name.StartsWith("GrayAlpha", StringComparison.Ordinal) ? "YA" : name.StartsWith("Gray", StringComparison.Ordinal) ? "Y" : name[..name.IndexOfAny(['8', '1', '3', '6'])].ToUpperInvariant();
        bool floating = name.Contains("Float", StringComparison.Ordinal);
        bool signed = name.Contains("Snorm", StringComparison.Ordinal);
        ulong[] words = channels.Select(channel => floating ? channel switch
        {
            'R' => width == 2 ? 0xC000UL : width == 4 ? 0xC0000000UL : 0xC000000000000000UL,
            'G' => width == 2 ? 0x3400UL : width == 4 ? 0x3E800000UL : 0x3FD0000000000000UL,
            'B' => width == 2 ? 0x4800UL : width == 4 ? 0x41000000UL : 0x4020000000000000UL,
            _ => width == 2 ? 0x3800UL : width == 4 ? 0x3F000000UL : 0x3FE0000000000000UL,
        } : signed ? channel switch { 'R' => width == 1 ? 193UL : 49153UL, 'G' => 17UL, 'B' => width == 1 ? 127UL : 32767UL, _ => 0UL }
        : channel switch { 'R' or 'Y' => width == 1 ? 17UL : 0x1234UL, 'G' => width == 1 ? 101UL : 0x5678UL, 'B' => width == 1 ? 231UL : 0x9ABCUL, _ => width == 1 ? 203UL : 0xDEF0UL }).ToArray();
        double max = width == 1 ? 255 : 65535;
        double r = floating ? -2 : signed ? (width == 1 ? -63d / 127 : -16383d / 32767) : (width == 1 ? 17d : 0x1234) / max;
        double g = floating ? 0.25 : signed ? 17d / (width == 1 ? 127 : 32767) : (width == 1 ? 101d : 0x5678) / max;
        double b = floating ? 8 : signed ? 1 : (width == 1 ? 231d : 0x9ABC) / max;
        double a = !channels.Contains('A') ? 1 : floating ? 0.5 : signed ? 0 : (width == 1 ? 203d : 0xDEF0) / max;
        if (channels.Contains('Y'))
        {
            g = b = r;
        }

        return (Words(words, width, order), new(r, g, b, a));
    }

    private static byte[] Words(ulong[] words, int width, ColorByteOrder order) => words.SelectMany(word => Enumerable.Range(0, width).Select(i => (byte)(word >> (8 * (order == ColorByteOrder.LittleEndian ? i : width - 1 - i))))).ToArray();
}
