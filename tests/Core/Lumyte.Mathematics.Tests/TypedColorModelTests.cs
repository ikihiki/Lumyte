using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks typed model storage, construction, and shared conversion contracts.</summary>
public sealed class TypedColorModelTests
{
    /// <summary>Checks canonical periodic hue without narrowing huge input.</summary>
    [Fact]
    public void CanonicalHueUsesExactRemainder()
    {
        BigInteger huge = BigInteger.Pow(10, 1000) + 17;
        Assert.Equal(huge % 360, new SHsva<BigInteger>(huge, 1, 1, 1).HueDegrees);
        Assert.Equal(360 - (huge % 360), new SHsla<BigInteger>(-huge, 1, 0, 1).HueDegrees);
        Assert.Equal(300d, new SHsla<double>(-60, 1, 0.5, 1).HueDegrees);
        Assert.Equal(0d, new SHsva<double>(720, 1, 1, 1).HueDegrees);
        Assert.Equal(double.MaxValue % 360, new SHsva<double>(double.MaxValue, 1, 1, 1).HueDegrees);
        Assert.Equal(0m, new SHsla<decimal>(-0.0000000000000000000000000001m, 0, 0, 1).HueDegrees);
    }

    /// <summary>Checks final hue rounding canonicalizes a full turn to zero.</summary>
    [Fact]
    public void RoundedFullTurnIsCanonicalZero()
    {
        Assert.Equal(0f, new SHsla<float>(-float.Epsilon, 1, 0.5f, 1).HueDegrees);
        Assert.Equal(0d, new SHsva<double>(-double.Epsilon, 1, 1, 1).HueDegrees);
        Assert.Equal((Half)0, new SHsva<Half>(-Half.Epsilon, (Half)1, (Half)1, (Half)1).HueDegrees);
    }

    /// <summary>Checks typed conversions reuse the exact RGBA factories.</summary>
    [Fact]
    public void ConversionUsesSharedFactories()
    {
        var hsl = new SHsla<decimal>(30m, 0.5m, 0.4m, 0.7m);
        var hsv = new SHsva<decimal>(30m, 0.5m, 0.4m, 0.7m);
        Assert.Equal(SRgba<double>.FromHslSrgbDegrees(30m, 0.5m, 0.4m, 0.7m), hsl.ToRgba<double>());
        Assert.Equal(SRgba<double>.FromHsvSrgbDegrees(30m, 0.5m, 0.4m, 0.7m), hsv.ToRgba<double>());
        Assert.Equal(new SRgba<float>(2 * float.Epsilon, 0, 0, 1), new SHsla<float>(0, 1, float.Epsilon, 1).ToRgba<float>());
        Assert.Equal(new SRgba<double>(0, 1, 1, 1), new SHsva<int>(180, 1, 1, 1).ToRgba<double>());
        Assert.Throws<ArithmeticException>(() => hsl.ToRgba<int>());
    }

    /// <summary>Checks model results are typed without a constructor RGB round trip.</summary>
    [Fact]
    public void RgbaConversionsReturnTypedResults()
    {
        SHsla<float> hsl = new SRgba<float>(1, 1, float.BitDecrement(1), 1).ToHslSrgbDegrees<float>();
        SHsva<int> hsv = new SRgba<int>(1, 0, 0, 1).ToHsvSrgbDegrees<int>();
        Assert.Equal(new SHsla<float>(60, 1, 1, 1), hsl);
        Assert.Equal(new SHsva<int>(0, 1, 1, 1), hsv);
        Assert.Equal((byte)120, new SHsla<byte>(120, 0, 0, 1).HueDegrees);
        Assert.Equal((byte)255, new SHsva<byte>(255, 1, 1, 1).HueDegrees);
    }

    /// <summary>Checks finite and unit validation at model construction.</summary>
    /// <param name="invalid">The invalid unit value.</param>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ConstructorsRejectInvalidUnits(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SHsla<double>(0, invalid, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SHsla<double>(0, 0, invalid, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SHsla<double>(0, 0, 0, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SHsva<double>(0, invalid, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SHsva<double>(0, 0, invalid, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SHsva<double>(0, 0, 0, invalid));
    }

    /// <summary>Checks nonfinite hues are rejected even for achromatic colors.</summary>
    /// <param name="invalid">The invalid hue.</param>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ConstructorsRejectNonfiniteHue(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SHsla<double>(invalid, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SHsva<double>(invalid, 0, 0, 1));
    }

    /// <summary>Checks default, periodic equality, and signed-zero hash contracts.</summary>
    [Fact]
    public void EqualityAndDefaultsUseNumericComponents()
    {
        Assert.Equal(new SHsla<double>(0, 0, 0, 0), default);
        Assert.Equal(new SHsva<double>(0, 0, 0, 0), default);
        Assert.Equal(default(SRgba<double>), default(SHsla<double>).ToRgba<double>());
        Assert.Equal(default(SRgba<double>), default(SHsva<double>).ToRgba<double>());
        Assert.Equal(new SHsla<double>(0, 0, 0, 0), new SHsla<double>(360, -0d, -0d, -0d));
        Assert.Equal(new SHsva<double>(0, 0, 0, 0).GetHashCode(), new SHsva<double>(-0d, -0d, -0d, -0d).GetHashCode());
    }
}
