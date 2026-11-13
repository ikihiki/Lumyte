using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Independent packed-word and typed-batch contract tests.</summary>
public sealed class TypedPackedPixelTests
{
    /// <summary>Both endian orders preserve arbitrary bits and all unused tails.</summary>
    [Fact]
    public void RawWordsAndBatchesPreserveBits()
    {
        foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
        {
            CheckRaw(new Rgb565(0xA53C), 0xA53C, order);
            CheckRaw(new Rgba4444(0xA53C), 0xA53C, order);
            CheckRaw(new Rgba5551(0xA53C), 0xA53C, order);
            CheckRaw(new Rgba1010102(0xC735A19B), 0xC735A19B, order);
            CheckRaw(new Bgra1010102(0xC735A19B), 0xC735A19B, order);
        }
    }

    /// <summary>Packed channel widths, swaps, exact ties and result failures use the existing kernel.</summary>
    [Fact]
    public void NormalizedConversionsMatchIndependentBitOracles()
    {
        Assert.Equal(new Rgba<double>(17d / 31, 42d / 63, 3d / 31, 1), new Rgb565((17 << 11) | (42 << 5) | 3).ToNormalized<double>());
        Assert.Equal(new Rgba<double>(1d / 15, 10d / 15, 3d / 15, 14d / 15), new Rgba4444(0x1A3E).ToNormalized<double>());
        Assert.Equal(new Rgba<double>(17d / 31, 10d / 31, 3d / 31, 1), new Rgba5551((17 << 11) | (10 << 6) | (3 << 1) | 1).ToNormalized<double>());
        const uint Word = 17u | (666u << 10) | (333u << 20) | (2u << 30);
        Assert.Equal(new Rgba<double>(17d / 1023, 666d / 1023, 333d / 1023, 2d / 3), new Rgba1010102(Word).ToNormalized<double>());
        Assert.Equal(new Rgba<double>(333d / 1023, 666d / 1023, 17d / 1023, 2d / 3), new Bgra1010102(Word).ToNormalized<double>());
        Assert.Equal((ushort)0x2A80, Rgba4444.FromNormalized(new Rgba<decimal>(0.1m, 0.7m, 0.5m, 0m)).PackedValue);
        Assert.Equal((ushort)0xF800, Rgb565.FromNormalized(new Rgba<BigInteger>(1, 0, 0, 1)).PackedValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgb565.FromNormalized(new Rgba<double>(0, 0, 0, 0)));
        Assert.Throws<ArithmeticException>(() => new Rgb565(1).ToNormalized<int>());
    }

    /// <summary>Raw batch preflight rejects short spans, invalid enums, checked sizes and every overlap.</summary>
    [Fact]
    public void PackedBatchPreflightIsAtomic()
    {
        Rgb565[] pixels = [new(0x1234), new(0x5678)];
        byte[] output = [0xAB, 0xAB, 0xAB, 0xAB, 0xAB];
        Assert.Throws<ArgumentException>(() => Color.WritePackedPixels<Rgb565>(pixels, output.AsSpan(0, 3), ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.WritePackedPixels<Rgb565>(pixels, output, (ColorByteOrder)99));
        Assert.All(output, value => Assert.Equal(0xAB, value));
        Assert.Throws<ArgumentException>(() => Color.ReadPackedPixels<Rgb565>(output.AsSpan(0, 3), pixels, ColorByteOrder.LittleEndian));
        Assert.Equal(new Rgb565[] { new(0x1234), new(0x5678) }, pixels);
        Assert.Throws<OverflowException>(() => Color.ReadPackedPixels<Rgb565>([], OversizedPixels(), ColorByteOrder.LittleEndian));
        Assert.Throws<OverflowException>(() => Color.WritePackedPixels<Rgb565>(OversizedPixels(), [], ColorByteOrder.LittleEndian));
        foreach (int offset in new[] { 0, 1, 3, 4, 7 })
        {
            byte[] storage = Enumerable.Repeat((byte)0xAB, 24).ToArray();
            byte[] before = (byte[])storage.Clone();
            Assert.Throws<ArgumentException>(() => Color.ReadPackedPixels<Rgb565>(storage.AsSpan(offset, 12), MemoryMarshal.Cast<byte, Rgb565>(storage.AsSpan(4, 4)), ColorByteOrder.LittleEndian));
            Assert.Throws<ArgumentException>(() => Color.WritePackedPixels<Rgb565>(MemoryMarshal.Cast<byte, Rgb565>(storage.AsSpan(4, 4)), storage.AsSpan(offset, 12), ColorByteOrder.LittleEndian));
            Assert.Equal(before, storage);
        }

        Color.ReadPackedPixels<Rgb565>([], [], ColorByteOrder.BigEndian);
        Color.WritePackedPixels<Rgb565>([], [], ColorByteOrder.BigEndian);
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.ReadPackedPixels<Rgb565>([], [], (ColorByteOrder)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.WritePackedPixels<Rgb565>([], [], (ColorByteOrder)99));
    }

    private static void CheckRaw<TPixel>(TPixel pixel, uint word, ColorByteOrder order)
        where TPixel : IPackedColorPixel<TPixel>
    {
        int size = TPixel.ByteSize;
        byte[] expected = Enumerable.Range(0, size).Select(i => (byte)(word >> (8 * (order == ColorByteOrder.LittleEndian ? i : size - 1 - i)))).ToArray();
        byte[] bytes = Enumerable.Repeat((byte)0xAB, size + 1).ToArray();
        Assert.Equal(size, pixel.Write(bytes, order));
        Assert.Equal(expected, bytes[..size]);
        Assert.Equal(0xAB, bytes[^1]);
        Assert.Equal(pixel, TPixel.Read(bytes, order));
        Assert.Throws<ArgumentException>(() => pixel.Write(bytes.AsSpan(0, size - 1), order));
        Assert.Throws<ArgumentException>(() => TPixel.Read(bytes.AsSpan(0, size - 1), order));
        Assert.Throws<ArgumentOutOfRangeException>(() => pixel.Write(bytes, (ColorByteOrder)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => TPixel.Read(bytes, (ColorByteOrder)99));
        Assert.Equal(expected, bytes[..size]);
        TPixel[] source = [pixel, pixel];
        byte[] batch = Enumerable.Repeat((byte)0xAB, (size * 2) + 1).ToArray();
        Color.WritePackedPixels<TPixel>(source, batch, order);
        Assert.Equal(expected.Concat(expected), batch[..^1]);
        Assert.Equal(0xAB, batch[^1]);
        var decoded = new TPixel[2];
        Color.ReadPackedPixels<TPixel>(batch, decoded, order);
        Assert.Equal(source, decoded);
    }

    private static Span<Rgb565> OversizedPixels() => MemoryMarshal.CreateSpan(ref Unsafe.NullRef<Rgb565>(), (int.MaxValue / 2) + 1);
}
