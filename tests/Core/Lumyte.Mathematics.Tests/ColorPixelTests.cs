using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Independent byte oracles for every format, endian and failure boundary.</summary>
public sealed class ColorPixelTests
{
    /// <summary>All 27 storage layouts are checked against independently assembled channel words.</summary>
    [Fact]
    public void AllFormatsAndEndianMatchKnownBytes()
    {
        Assert.Equal(27, Enum.GetValues<ColorPixelFormat>().Length);
        foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
        {
            foreach (ColorPixelFormat format in Enum.GetValues<ColorPixelFormat>())
            {
                (byte[] bytes, Rgba<double> expected) = Oracle(format, order);
                byte[] source = [.. bytes, 0xED, 0xFE];
                Assert.Equal(expected, Rgba<double>.DecodePixel(source, format, order));
                byte[] output = Enumerable.Repeat((byte)0xCD, bytes.Length + 3).ToArray();
                Assert.Equal(bytes.Length, expected.WritePixel(output, format, order));
                Assert.Equal(bytes, output[..bytes.Length]);
                Assert.All(output[bytes.Length..], value => Assert.Equal(0xCD, value));
                var decoded = new Rgba<double>[3];
                Color.DecodePixels<double>([.. bytes, .. bytes, .. bytes, 0xEF], decoded, format, order);
                Assert.All(decoded, value => Assert.Equal(expected, value));
                byte[] batch = Enumerable.Repeat((byte)0xCD, (bytes.Length * 3) + 1).ToArray();
                Color.WritePixels<double>(decoded, batch, format, order);
                Assert.Equal(bytes.Concat(bytes).Concat(bytes), batch[..^1]);
                Assert.Equal(0xCD, batch[^1]);
            }
        }
    }

    /// <summary>SNORM minimum decodes to -1, whose canonical encoding is the symmetric minimum.</summary>
    /// <param name="format">The signed component format.</param>
    /// <param name="width">The component byte width.</param>
    [Theory]
    [InlineData(ColorPixelFormat.Rgba8Snorm, 1)]
    [InlineData(ColorPixelFormat.Rgba16Snorm, 2)]
    public void SnormMinimumAndUnsignedFailure(ColorPixelFormat format, int width)
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
            foreach ((ColorPixelFormat format, int width) in new[] { (ColorPixelFormat.Rgba16Float, 2), (ColorPixelFormat.Rgba32Float, 4), (ColorPixelFormat.Rgba64Float, 8) })
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
        new Rgba<decimal>(1.00048828125m, 1.00146484375m, 0, 1).WritePixel(rounded, ColorPixelFormat.Rgba16Float, ColorByteOrder.LittleEndian);
        Assert.Equal(Words([0x3C00, 0x3C02, 0, 0x3C00], 2, ColorByteOrder.LittleEndian), rounded);
        Assert.Throws<ArithmeticException>(() => Rgba<int>.DecodePixel(Words([0x3E00, 0, 0, 0], 2, ColorByteOrder.LittleEndian), ColorPixelFormat.Rgba16Float, ColorByteOrder.LittleEndian));
    }

    /// <summary>Single-pixel validation is atomic for every format and endian.</summary>
    [Fact]
    public void SinglePixelFailuresAreAtomic()
    {
        foreach (ColorPixelFormat format in Enum.GetValues<ColorPixelFormat>())
        {
            foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
            {
                (byte[] bytes, Rgba<double> color) = Oracle(format, order);
                byte[] output = Enumerable.Repeat((byte)0xAB, bytes.Length + 1).ToArray();
                byte[] before = (byte[])output.Clone();
                Assert.Throws<ArgumentException>(() => color.WritePixel(output.AsSpan(0, bytes.Length - 1), format, order));
                Assert.Equal(before, output);
                Assert.Throws<ArgumentException>(() => Rgba<double>.DecodePixel(bytes.AsSpan(1), format, order));
                Assert.Throws<ArgumentOutOfRangeException>(() => color.WritePixel(output, format, (ColorByteOrder)99));
                Assert.Throws<ArgumentOutOfRangeException>(() => color.WritePixel(output, (ColorPixelFormat)99, order));
                Assert.Equal(before, output);
                if (format.ToString().StartsWith("Gray", StringComparison.Ordinal))
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => new Rgba<double>(0, 1, 0, 1).WritePixel(output, format, order));
                    Assert.Equal(before, output);
                }

                bool noAlpha = !format.ToString().Contains("Rgba", StringComparison.Ordinal) && !format.ToString().Contains("Bgra", StringComparison.Ordinal)
                    && !format.ToString().Contains("Argb", StringComparison.Ordinal) && !format.ToString().Contains("Abgr", StringComparison.Ordinal)
                    && !format.ToString().Contains("Alpha", StringComparison.Ordinal);
                if (noAlpha)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => color.WithAlpha(0).WritePixel(output, format, order));
                    Assert.Equal(before, output);
                }
                else if (!format.ToString().Contains("Float", StringComparison.Ordinal))
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => color.WithAlpha(2).WritePixel(output, format, order));
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
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.WritePixels<double>(pixels, bytes, ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bytes[..4]);
        Assert.All(bytes[4..], value => Assert.Equal(0xAB, value));
        byte[] before = (byte[])bytes.Clone();
        Assert.Throws<ArgumentException>(() => Color.WritePixels<double>(pixels, bytes.AsSpan(0, 11), ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian));
        Assert.Equal(before, bytes);
        Rgba<int>[] destination = [new(7, 7, 7, 7), new(7, 7, 7, 7), new(7, 7, 7, 7)];
        byte[] codes = [255, 0, 0, 255, 255, 128, 0, 255, 0, 0, 0, 255];
        Assert.Throws<ArithmeticException>(() => Color.DecodePixels<int>(codes, destination, ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian));
        Assert.Equal(new Rgba<int>(1, 0, 0, 1), destination[0]);
        Assert.Equal(new Rgba<int>(7, 7, 7, 7), destination[1]);
        Assert.Equal(destination[1], destination[2]);
        var saved = (Rgba<int>[])destination.Clone();
        Assert.Throws<ArgumentException>(() => Color.DecodePixels<int>(codes.AsSpan(1), destination, ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.DecodePixels<int>(codes, destination, (ColorPixelFormat)99, ColorByteOrder.LittleEndian));
        Assert.Equal(saved, destination);
        Color.DecodePixels<int>([], [], ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian);
        Color.WritePixels<int>([], [], ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian);
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.DecodePixels<int>([], [], (ColorPixelFormat)99, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.WritePixels<int>([], [], ColorPixelFormat.Rgba8Unorm, (ColorByteOrder)99));
    }

    /// <summary>Same-start and shifted overlaps, including unused byte tails, fail before any writes.</summary>
    [Fact]
    public void BatchRejectsAllOverlaps()
    {
        foreach (int offset in new[] { 0, 1, 15, 16, 31 })
        {
            byte[] storage = Enumerable.Repeat((byte)0xAB, 96).ToArray();
            byte[] before = (byte[])storage.Clone();
            Assert.Throws<ArgumentException>(() => Color.DecodePixels<int>(storage.AsSpan(offset, 40), MemoryMarshal.Cast<byte, Rgba<int>>(storage.AsSpan(16, 32)), ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian));
            Assert.Equal(before, storage);
            Assert.Throws<ArgumentException>(() => Color.WritePixels<int>(MemoryMarshal.Cast<byte, Rgba<int>>(storage.AsSpan(16, 32)), storage.AsSpan(offset, 40), ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian));
            Assert.Equal(before, storage);
        }

        byte[] adjacent = new byte[48];
        Color.DecodePixels<int>(adjacent.AsSpan(0, 4), MemoryMarshal.Cast<byte, Rgba<int>>(adjacent.AsSpan(4, 16)), ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian);
        Color.WritePixels<int>(MemoryMarshal.Cast<byte, Rgba<int>>(adjacent.AsSpan(4, 16)), adjacent.AsSpan(20, 4), ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian);
    }

    /// <summary>UNORM ties use exact mathematical values, including decimal midpoint neighbors.</summary>
    [Fact]
    public void QuantizationTiesAndIntegralDecode()
    {
        byte[] output = new byte[4];
        new Rgba<decimal>(0.1m, 0.3m, 0.5m, 0.7m).WritePixel(output, ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian);
        Assert.Equal(new byte[] { 26, 76, 128, 178 }, output);
        Assert.Throws<ArithmeticException>(() => Rgba<int>.DecodePixel(new byte[] { 1, 0, 0, 255 }, ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian));
        Assert.Equal(new Rgba<BigInteger>(1, 0, 0, 1), Rgba<BigInteger>.DecodePixel(new byte[] { 255, 0, 0, 255 }, ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian));
    }

    /// <summary>Checked sizes fail before storage access, and reference-containing BigInteger is supported.</summary>
    [Fact]
    public void CheckedSizesAndReferenceContainingStorage()
    {
        Assert.Throws<OverflowException>(() => Color.DecodePixels<double>([], OversizedPixels(), ColorPixelFormat.Rgba64Float, ColorByteOrder.LittleEndian));
        Assert.Throws<OverflowException>(() => Color.WritePixels<double>(OversizedPixels(), [], ColorPixelFormat.Rgba64Float, ColorByteOrder.LittleEndian));
        Rgba<BigInteger>[] integers = [new(1, 0, 1, 1), new(0, 1, 0, 1)];
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<Rgba<BigInteger>>());
        byte[] bytes = new byte[8];
        Color.WritePixels<BigInteger>(integers, bytes, ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian);
        var output = new Rgba<BigInteger>[2];
        Color.DecodePixels<BigInteger>(bytes, output, ColorPixelFormat.Rgba8Unorm, ColorByteOrder.LittleEndian);
        Assert.Equal(integers, output);
    }

    private static Span<Rgba<double>> OversizedPixels() => MemoryMarshal.CreateSpan(ref Unsafe.NullRef<Rgba<double>>(), (int.MaxValue / 32) + 1);

    private static (byte[] Bytes, Rgba<double> Color) Oracle(ColorPixelFormat format, ColorByteOrder order)
    {
        string name = format.ToString();
        if (format is ColorPixelFormat.Rgb565Unorm)
        {
            return (Words([(17UL << 11) | (42UL << 5) | 3], 2, order), new(17d / 31, 42d / 63, 3d / 31, 1));
        }

        if (format is ColorPixelFormat.Rgba4444Unorm)
        {
            return (Words([0x1A3E], 2, order), new(1d / 15, 10d / 15, 3d / 15, 14d / 15));
        }

        if (format is ColorPixelFormat.Rgba5551Unorm)
        {
            return (Words([(17UL << 11) | (10UL << 6) | (3UL << 1) | 1], 2, order), new(17d / 31, 10d / 31, 3d / 31, 1));
        }

        if (format is ColorPixelFormat.Rgba1010102Unorm or ColorPixelFormat.Bgra1010102Unorm)
        {
            bool bgra = format == ColorPixelFormat.Bgra1010102Unorm;
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
