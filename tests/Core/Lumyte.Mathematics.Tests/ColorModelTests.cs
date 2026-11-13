using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks exact HSL and HSV formulas and final rounding.</summary>
public sealed class ColorModelTests
{
    /// <summary>Verifies sector boundaries.</summary>
    /// <param name="hue">The test hue.</param>
    /// <param name="red">The test red.</param>
    /// <param name="green">The test green.</param>
    /// <param name="blue">The test blue.</param>
    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(60, 1, 1, 0)]
    [InlineData(120, 0, 1, 0)]
    [InlineData(180, 0, 1, 1)]
    [InlineData(240, 0, 0, 1)]
    [InlineData(300, 1, 0, 1)]
    public void SectorBoundaries(int hue, int red, int green, int blue)
    {
        var expected = new Rgba<double>(red, green, blue, 0.25);
        Assert.Equal(expected, Rgba<double>.FromHsvSrgbDegrees((double)hue, 1, 1, 0.25));
        Assert.Equal(expected, Rgba<double>.FromHslSrgbDegrees((double)hue, 1, 0.5, 0.25));
        Assert.Equal(new Hsva<double>((double)hue, 1d, 1d, 0.25), expected.ToHsvSrgbDegrees<double>());
        Assert.Equal(new Hsla<double>((double)hue, 1d, 0.5, 0.25), expected.ToHslSrgbDegrees<double>());
    }

    /// <summary>Verifies hue is periodic.</summary>
    /// <param name="input">The test input.</param>
    /// <param name="expected">The test expected.</param>
    [Theory]
    [InlineData(-60, 300)]
    [InlineData(360, 0)]
    [InlineData(720, 0)]
    [InlineData(-720, 0)]
    [InlineData(390, 30)]
    public void HueIsPeriodic(double input, double expected)
    {
        Assert.Equal(
            Rgba<double>.FromHsvSrgbDegrees(expected, 1, 1, 1),
            Rgba<double>.FromHsvSrgbDegrees(input, 1, 1, 1));
        Assert.Equal(
            Rgba<double>.FromHslSrgbDegrees(expected, 1, 0.5, 1),
            Rgba<double>.FromHslSrgbDegrees(input, 1, 0.5, 1));
    }

    /// <summary>Verifies huge hues use exact remainders.</summary>
    [Fact]
    public void HugeHuesUseExactRemainders()
    {
        BigInteger hue = BigInteger.Pow(10, 1000) + 17;
        Assert.Equal(
            Rgba<double>.FromHsvSrgbDegrees((double)(hue % 360), 1, 1, 1),
            Rgba<double>.FromHsvSrgbDegrees(hue, BigInteger.One, BigInteger.One, BigInteger.One));
        Assert.Equal(
            Rgba<double>.FromHsvSrgbDegrees(double.MaxValue % 360, 1, 1, 1),
            Rgba<double>.FromHsvSrgbDegrees(double.MaxValue, 1, 1, 1));
    }

    /// <summary>Verifies achromatic hue and saturation are zero.</summary>
    /// <param name="gray">The test gray.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void AchromaticHueAndSaturationAreZero(double gray)
    {
        var color = new Rgba<double>(gray, gray, gray, 0.75);
        Assert.Equal(new Hsla<double>(0d, 0d, gray, 0.75), color.ToHslSrgbDegrees<double>());
        Assert.Equal(new Hsva<double>(0d, 0d, gray, 0.75), color.ToHsvSrgbDegrees<double>());
    }

    /// <summary>Verifies white neighbor does not round lightness before saturation.</summary>
    [Fact]
    public void WhiteNeighborDoesNotRoundLightnessBeforeSaturation()
    {
        var color = new Rgba<float>(1, 1, float.BitDecrement(1), 1);
        Assert.Equal(new Hsla<float>(60f, 1f, 1f, 1f), color.ToHslSrgbDegrees<float>());
        var decimalColor = new Rgba<decimal>(1, 1, 0.9999999999999999999999999999m, 1);
        Assert.Equal(new Hsla<decimal>(60m, 1m, 1m, 1m), decimalColor.ToHslSrgbDegrees<decimal>());
    }

    /// <summary>Verifies subnormals have exact chroma and final tie even rounding.</summary>
    [Fact]
    public void SubnormalsHaveExactChromaAndFinalTieEvenRounding()
    {
        var color = new Rgba<float>(3 * float.Epsilon, float.Epsilon, float.Epsilon, 1);
        Assert.Equal(new Hsla<float>(0f, 0.5f, 2 * float.Epsilon, 1f), color.ToHslSrgbDegrees<float>());
        Assert.Equal(
            new Rgba<float>(2 * float.Epsilon, 0, 0, 1),
            Rgba<float>.FromHslSrgbDegrees(0f, 1f, float.Epsilon, 1f));
        Assert.Equal(
            new Rgba<float>(0, float.Epsilon, 0, 1),
            Rgba<float>.FromHsvSrgbDegrees(90f, 1f, float.Epsilon, 1f));
        Assert.Equal(
            new Hsva<double>(0d, 1d, double.Epsilon, 1d),
            new Rgba<double>(double.Epsilon, 0, 0, 1).ToHsvSrgbDegrees<double>());
    }

    /// <summary>Verifies hue rounding to full turn wraps to zero.</summary>
    [Fact]
    public void HueRoundingToFullTurnWrapsToZero()
    {
        Assert.Equal(0f, new Rgba<double>(1, 0, 1e-10, 1).ToHsvSrgbDegrees<float>().HueDegrees);
        Assert.Equal((Half)0, new Rgba<double>(1, 0, 1e-5, 1).ToHslSrgbDegrees<Half>().HueDegrees);
    }

    /// <summary>Verifies decimal sector interior is exact.</summary>
    [Fact]
    public void DecimalSectorInteriorIsExact()
    {
        var expected = new Rgba<decimal>(0.6m, 0.4m, 0.2m, 0.7m);
        Assert.Equal(expected, Rgba<decimal>.FromHsvSrgbDegrees(30m, 2m / 3m, 0.6m, 0.7m));
        Assert.Equal(expected, Rgba<decimal>.FromHslSrgbDegrees(30m, 0.5m, 0.4m, 0.7m));
        Assert.Equal(new Hsla<decimal>(30m, 0.5m, 0.4m, 0.7m), expected.ToHslSrgbDegrees<decimal>());
    }

    /// <summary>Verifies integral results reject fractions rather than truncate.</summary>
    [Fact]
    public void IntegralResultsRejectFractionsRatherThanTruncate()
    {
        Assert.Equal(new Rgba<int>(1, 0, 0, 1), Rgba<int>.FromHsvSrgbDegrees(0, 1, 1, 1));
        Assert.Equal(new Hsva<int>(0, 1, 1, 1), new Rgba<int>(1, 0, 0, 1).ToHsvSrgbDegrees<int>());
        Assert.Throws<ArithmeticException>(() => Rgba<int>.FromHsvSrgbDegrees(30d, 1, 1, 1));
        Assert.Throws<ArithmeticException>(() => Rgba<int>.FromHslSrgbDegrees(0d, 1, 0.5, 0.5));
        Assert.Throws<ArithmeticException>(() => new Rgba<int>(1, 0, 0, 1).ToHslSrgbDegrees<int>());
        Assert.Throws<OverflowException>(() => new Rgba<int>(1, 0, 1, 1).ToHsvSrgbDegrees<byte>());
    }

    /// <summary>Verifies nonfinite hue is rejected.</summary>
    /// <param name="hue">The test hue.</param>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteHueIsRejected(double hue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.FromHslSrgbDegrees(hue, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.FromHsvSrgbDegrees(hue, 0, 0, 1));
    }

    /// <summary>Verifies model unit inputs are validated.</summary>
    /// <param name="invalid">The test invalid.</param>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ModelUnitInputsAreValidated(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.FromHslSrgbDegrees(0, invalid, 0.5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.FromHslSrgbDegrees(0, 1, invalid, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.FromHslSrgbDegrees(0, 1, 0.5, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.FromHsvSrgbDegrees(0, invalid, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.FromHsvSrgbDegrees(0, 1, invalid, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgba<double>.FromHsvSrgbDegrees(0, 1, 1, invalid));
    }

    /// <summary>Verifies non unit stored components are rejected.</summary>
    [Fact]
    public void NonUnitStoredComponentsAreRejected()
    {
        Rgba<double>[] colors = [new(-0.1, 0, 0, 1), new(0, 1.1, 0, 1), new(0, 0, 2, 1), new(0, 0, 0, 2)];
        foreach (Rgba<double> color in colors)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => color.ToHslSrgbDegrees<double>());
            Assert.Throws<ArgumentOutOfRangeException>(() => color.ToHsvSrgbDegrees<double>());
        }
    }
}
