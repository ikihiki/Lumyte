using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Independent adversarial checks for typed pixel numerical and batch contracts.</summary>
public sealed class TypedColorNumericalReviewTests
{
    /// <summary>Every signed byte, including both encodings of minus one, has the specified numerical image.</summary>
    [Fact]
    public void EverySnormByteHasIndependentDecodeAndCanonicalEncode()
    {
        for (int code = -128; code <= 127; code++)
        {
            byte raw = unchecked((byte)code);
            byte[] input = [raw, raw, raw, raw];
            foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
            {
                double expected = Math.Max(-1d, code / 127d);
                var color = SrgbRgba<double>.DecodePixel(input, ColorComponentEncoding.Snorm8, order);
                Assert.Equal(new SrgbRgba<double>(expected, expected, expected, expected), color);
                byte[] output = new byte[4];
                color.WritePixel(output, ColorComponentEncoding.Snorm8, order);
                Assert.All(output, value => Assert.Equal(unchecked((byte)Math.Max(-127, code)), value));
            }
        }
    }

    /// <summary>Bit-isolated packed words test each channel independently rather than only round trips.</summary>
    [Fact]
    public void PackedBitWalkMatchesIndependentMasksInBothOrders()
    {
        foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
        {
            for (int bit = 0; bit < 32; bit++)
            {
                uint word = 1u << bit;
                byte[] bytes = Enumerable.Range(0, 4).Select(i => (byte)(word >> (8 * (order == ColorByteOrder.LittleEndian ? i : 3 - i)))).ToArray();
                SrgbRgba<double> rgba = RawRgba1010102.Read(bytes, order).InterpretSrgb<double>();
                Assert.Equal(new SrgbRgba<double>((word & 1023) / 1023d, ((word >> 10) & 1023) / 1023d, ((word >> 20) & 1023) / 1023d, (word >> 30) / 3d), rgba);
                SrgbRgba<double> bgra = RawBgra1010102.Read(bytes, order).InterpretSrgb<double>();
                Assert.Equal(new SrgbRgba<double>(rgba.B, rgba.G, rgba.R, rgba.A), bgra);
                Assert.Equal(word, RawRgba1010102.FromSrgb(rgba).PackedValue);
                Assert.Equal(word, RawBgra1010102.FromSrgb(bgra).PackedValue);
                if (bit < 16)
                {
                    ushort small = (ushort)word;
                    byte[] shortBytes = order == ColorByteOrder.LittleEndian ? [(byte)small, (byte)(small >> 8)] : [(byte)(small >> 8), (byte)small];
                    Assert.Equal(new SrgbRgba<double>((small >> 11) / 31d, ((small >> 5) & 63) / 63d, (small & 31) / 31d, 1), RawRgb565.Read(shortBytes, order).InterpretSrgb<double>());
                    Assert.Equal(new SrgbRgba<double>((small >> 12) / 15d, ((small >> 8) & 15) / 15d, ((small >> 4) & 15) / 15d, (small & 15) / 15d), RawRgba4444.Read(shortBytes, order).InterpretSrgb<double>());
                    Assert.Equal(new SrgbRgba<double>((small >> 11) / 31d, ((small >> 6) & 31) / 31d, ((small >> 1) & 31) / 31d, small & 1), RawRgba5551.Read(shortBytes, order).InterpretSrgb<double>());
                }
            }
        }
    }

    /// <summary>Raw gray codes supply maximum alpha only during named normalization.</summary>
    [Fact]
    public void HugeGrayMaximumAndAlphaPoliciesAreIndependent()
    {
        BigInteger maximum = BigInteger.One << 20000;
        var gray = new SrgbGray<BigInteger>(maximum / 2);
        Assert.Equal(new SrgbRgba<double>(0.5, 0.5, 0.5, 1), gray.NormalizeUnorm<double>(maximum));
        Assert.Equal(maximum, gray.ToRgba(maximum).A);
        Assert.Throws<ArgumentOutOfRangeException>(() => SrgbGray<BigInteger>.FromRgba(gray.ToRgba(maximum)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SrgbGrayAlpha<int>.FromRgba(new(1, 0, 1, 1)));
        Assert.Equal(new SrgbRgba<double>(0.5, 0.5, 0.5, 0.25), new SrgbGrayAlpha<BigInteger>(maximum / 2, maximum / 4).NormalizeUnorm<double>(maximum));
    }

    /// <summary>Half storage matches the BCL's independent represented values and signed-zero bits.</summary>
    [Fact]
    public void HalfStorageSweepPreservesFiniteBits()
    {
        foreach (int bits in Enumerable.Range(0, (ushort.MaxValue / 31) + 1).Select(i => i * 31).Append(0x8000))
        {
            Half value = BitConverter.UInt16BitsToHalf((ushort)bits);
            if (!Half.IsFinite(value))
            {
                continue;
            }

            byte[] bytes = [(byte)bits, (byte)(bits >> 8), 0, 0, 0, 0, 0, 0];
            var color = SrgbRgba<double>.DecodePixel(bytes, ColorComponentEncoding.Float16, ColorByteOrder.LittleEndian);
            Assert.Equal(BitConverter.DoubleToUInt64Bits((double)value), BitConverter.DoubleToUInt64Bits(color.R));
            byte[] output = new byte[8];
            color.WritePixel(output, ColorComponentEncoding.Float16, ColorByteOrder.LittleEndian);
            Assert.Equal(bytes, output);
        }
    }

    /// <summary>Huge signed hue reduction and narrow integer model outputs obey their actual representability.</summary>
    [Fact]
    public void ModelHugeAndNarrowTypeEdges()
    {
        BigInteger huge = (BigInteger.One << 20000) + 37;
        BigInteger expected = ((-huge % 360) + 360) % 360;
        var model = new SrgbHsva<BigInteger>(-huge, 1, 1, 1);
        Assert.Equal(expected, model.HueDegrees);
        Assert.Equal(SrgbRgba<double>.FromHsvSrgbDegrees((int)expected, 1, 1, 1), model.ToRgba<double>());
        Assert.Throws<OverflowException>(() => new SrgbHsla<sbyte>(-1, 0, 0, 1));
        Assert.Throws<OverflowException>(() => new SrgbRgba<byte>(1, 0, 1, 1).ToHsvSrgbDegrees<byte>());
        Assert.Equal(new SrgbRgba<byte>(0, 0, 1, 1), new SrgbHsva<byte>(240, 1, 1, 1).ToRgba<byte>());
        Assert.Throws<ArithmeticException>(() => new SrgbHsva<byte>(0, 1, 1, 1).ToRgba<byte>().ToHslSrgbDegrees<byte>());
    }

    /// <summary>Managed BigInteger output commits a complete prefix, never a partially decoded pixel.</summary>
    [Fact]
    public void ManagedBigIntegerFailureIsPixelAtomic()
    {
        SrgbRgba<BigInteger>[] output = [new(7, 7, 7, 7), new(8, 8, 8, 8), new(9, 9, 9, 9)];
        var saved = (SrgbRgba<BigInteger>[])output.Clone();
        byte[] input = [255, 0, 255, 255, 255, 0, 1, 255, 0, 0, 0, 255];
        Assert.Throws<ArithmeticException>(() => Color.DecodePixels<SrgbRgba<BigInteger>>(input, output, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Equal(new SrgbRgba<BigInteger>(1, 0, 1, 1), output[0]);
        Assert.Equal(saved[1], output[1]);
        Assert.Equal(saved[2], output[2]);
    }

    /// <summary>Overlap checks operate on managed pixel storage without rejecting all managed values.</summary>
    [Fact]
    public void ManagedStorageRejectsSameStartAndTailOverlap()
    {
        CheckManagedOverlap<BigInteger>();
        Type custom = typeof(CustomNumberGeometryTests).GetNestedType("ManagedDecimal", BindingFlags.NonPublic)!;
        typeof(TypedColorNumericalReviewTests).GetMethod(nameof(CheckManagedOverlap), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(custom).Invoke(null, null);
    }

    private static void CheckManagedOverlap<T>()
        where T : INumber<T>
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SrgbRgba<T>>());
        SrgbRgba<T>[] pixels = [new(T.One, T.Zero, T.One, T.One), new(T.One, T.Zero, T.One, T.One), new(T.One, T.Zero, T.One, T.One)];
        var before = (SrgbRgba<T>[])pixels.Clone();
        int size = Unsafe.SizeOf<SrgbRgba<T>>();
        foreach (int offset in new[] { 0, size - 1 })
        {
            Assert.Throws<ArgumentException>(() => Color.DecodePixels<SrgbRgba<T>>(AliasBytes(pixels, offset), pixels.AsSpan(0, 1), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
            Assert.Throws<ArgumentException>(() => Color.WritePixels<SrgbRgba<T>>(pixels.AsSpan(0, 1), AliasBytes(pixels, offset), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
            Assert.Equal(before, pixels);
        }

        byte[] separate = new byte[12];
        Color.WritePixels<SrgbRgba<T>>(pixels, separate, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        var decoded = new SrgbRgba<T>[3];
        Color.DecodePixels<SrgbRgba<T>>(separate, decoded, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Assert.Equal(pixels, decoded);
        SrgbRgba<T>[] failing = [pixels[0], new(T.One, T.Zero, T.One, T.CreateChecked(2)), pixels[2]];
        byte[] target = Enumerable.Repeat((byte)0xAB, 15).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.WritePixels<SrgbRgba<T>>(failing, target, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Equal(new byte[] { 255, 0, 255, 255 }, target[..4]);
        Assert.All(target[4..], value => Assert.Equal(0xAB, value));
        byte[] saved = (byte[])target.Clone();
        Assert.Throws<ArgumentException>(() => Color.WritePixels<SrgbRgba<T>>(failing, target.AsSpan(0, 11), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.WritePixels<SrgbRgba<T>>(pixels, target, ColorComponentEncoding.Unorm8, (ColorByteOrder)99));
        Assert.Equal(saved, target);
    }

    private static Span<byte> AliasBytes<T>(T[] pixels, int offset) => MemoryMarshal.CreateSpan(ref Unsafe.Add(ref Unsafe.As<T, byte>(ref pixels[0]), offset), 4);
}
