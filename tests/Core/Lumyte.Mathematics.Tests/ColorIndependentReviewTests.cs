using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Independent adversarial checks of exact rounding and reference-containing alias safety.</summary>
public sealed class ColorIndependentReviewTests
{
    /// <summary>Bits far below double precision still decide which side of a float midpoint is selected.</summary>
    [Fact]
    public void HugeNormalizationDistinguishesMidpointPerturbations()
    {
        BigInteger denominator = BigInteger.One << 2000;
        BigInteger midpoint = (denominator / 2) + (denominator >> 25);
        var input = new SRgba<BigInteger>(midpoint - 1, midpoint, midpoint + 1, denominator);
        Assert.Equal(new SRgba<float>(0.5f, 0.5f, float.BitIncrement(0.5f), 1), input.NormalizeUnorm<float>(denominator));
    }

    /// <summary>An exact represented binary fraction times a huge bound is not a rounded double product.</summary>
    [Fact]
    public void HugeQuantizationUsesAllRepresentedInputBits()
    {
        BigInteger maximum = (BigInteger.One << 2000) + 1;
        var input = new SRgba<double>(0.5, double.BitIncrement(0.5), double.Epsilon, 1);
        BigInteger midpoint = BigInteger.One << 1999;
        Assert.Equal(new SRgba<BigInteger>(midpoint, midpoint + (BigInteger.One << 1947) + 1, BigInteger.One << 926, maximum), input.QuantizeUnorm(maximum));
    }

    /// <summary>IEEE pixel decoding preserves finite negative zero rather than applying arithmetic to storage.</summary>
    /// <param name="format">The IEEE storage format.</param>
    /// <param name="width">Bytes per component.</param>
    [Theory]
    [InlineData(ColorComponentEncoding.Float16, 2)]
    [InlineData(ColorComponentEncoding.Float32, 4)]
    [InlineData(ColorComponentEncoding.Float64, 8)]
    public void FloatPixelDecodePreservesNegativeZero(ColorComponentEncoding format, int width)
    {
        byte[] bytes = new byte[4 * width];
        bytes[width - 1] = 0x80;
        var decoded = SRgba<double>.DecodePixel(bytes, format, ColorByteOrder.LittleEndian);
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(decoded.R));
    }

    /// <summary>Aliasing managed numeric storage is rejected before any conversion or write.</summary>
    [Fact]
    public void ReferenceContainingStorageRejectsEveryAlias()
    {
        BigInteger huge = BigInteger.One << 500;
        var expected = new SRgba<BigInteger>(huge, huge, huge, huge);
        SRgba<BigInteger>[] pixels = [expected, expected];
        int size = Unsafe.SizeOf<SRgba<BigInteger>>();
        foreach (int offset in new[] { 0, 1, size - 1, (2 * size) - 4 })
        {
            Assert.Throws<ArgumentException>(() => WriteAliased(pixels, offset));
            Assert.Equal(expected, pixels[0]);
            Assert.Equal(expected, pixels[1]);
            Assert.Throws<ArgumentException>(() => DecodeAliased(pixels, offset));
            Assert.Equal(expected, pixels[0]);
            Assert.Equal(expected, pixels[1]);
        }
    }

    /// <summary>Decimal minimum-step ties and subnormal hue evaluation use the final result lattice.</summary>
    [Fact]
    public void DecimalAndSubnormalModelsRoundOnlyFinalComponents()
    {
        var maximum = BigInteger.Pow(10, 29);
        var codes = new SRgba<BigInteger>(5, 15, 25, maximum);
        Assert.Equal(new SRgba<decimal>(0, 0.0000000000000000000000000002m, 0.0000000000000000000000000002m, 1), codes.NormalizeUnorm<decimal>(maximum));
        var color = new SRgba<double>(1, 0, double.Epsilon, 1);
        Assert.Equal(0d, color.ToHsvSrgbDegrees<double>().HueDegrees);
        Assert.Equal(0d, color.ToHslSrgbDegrees<double>().HueDegrees);
        Assert.Equal(new SRgba<double>(0, double.Epsilon, 0, 1), SRgba<double>.FromHsvSrgbDegrees(90d, 1d, double.Epsilon, 1d));
    }

    /// <summary>Negative SNORM ties select the even symmetric code and conversion failures are atomic.</summary>
    [Fact]
    public void SignedQuantizationIsSymmetricAndAtomic()
    {
        byte[] output = [0xAB, 0xAB, 0xAB, 0xAB, 0xAB];
        new SRgba<decimal>(-0.5m, 0.5m, -1m, 1m).WritePixel(output, ColorComponentEncoding.Snorm8, ColorByteOrder.LittleEndian);
        Assert.Equal(new byte[] { 192, 64, 129, 127, 0xAB }, output);
        byte[] before = (byte[])output.Clone();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SRgba<double>(1, 2, double.MaxValue, 1).WritePixel(output.AsSpan(0, 4), ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Equal(before, output);
    }

    private static void WriteAliased(SRgba<BigInteger>[] pixels, int offset)
    {
        ref byte first = ref Unsafe.As<SRgba<BigInteger>, byte>(ref pixels[0]);
        Span<byte> bytes = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref first, offset), 8);
        Color.WritePixels<SRgba<BigInteger>>(pixels, bytes, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
    }

    private static void DecodeAliased(SRgba<BigInteger>[] pixels, int offset)
    {
        ref byte first = ref Unsafe.As<SRgba<BigInteger>, byte>(ref pixels[0]);
        ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref first, offset), 8);
        Color.DecodePixels<SRgba<BigInteger>>(bytes, pixels, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
    }
}
