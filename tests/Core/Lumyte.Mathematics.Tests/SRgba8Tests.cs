using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks semantic byte codes independently of the native conversion implementation.</summary>
public sealed class SRgba8Tests
{
    /// <summary>Every code in every channel matches the retained exact rational normalization bit for bit.</summary>
    [Fact]
    public void AllByteCodesMatchExactNormalizationInEveryComponent()
    {
        for (int component = 0; component < 4; component++)
        {
            for (int code = 0; code <= byte.MaxValue; code++)
            {
                SRgba8 input = InComponent(component, (byte)code);
                SRgba<float> actual = input;
                AssertBits(input.ToCodes().NormalizeUnorm<float>(255), actual);
                AssertBits(input.ToNormalized<float>(), actual);
                Assert.Equal(input, (SRgba8)actual);
                Assert.Equal(input, SRgba8.FromNormalized(actual));
                Assert.Equal(input, SRgba8.FromCodes(input.ToCodes()));
            }
        }
    }

    /// <summary>All published palette entries have normalized float and raw-code round trips.</summary>
    [Fact]
    public void EveryPaletteEntryRoundTripsWithoutChangingRawCasts()
    {
        Assert.Equal(149, CssColors.All.Count);
        foreach ((string _, SRgba8 input) in CssColors.All)
        {
            SRgba<byte> codes = input.ToCodes();
            SRgba<float> actual = input;
            AssertBits(codes.NormalizeUnorm<float>(255), actual);
            Assert.Equal(input, (SRgba8)actual);
            Assert.Equal(input, SRgba8.FromNormalized(actual));
            Assert.Equal(input, SRgba8.FromCodes(codes));
            Assert.Equal(new SRgba<float>(codes.R, codes.G, codes.B, codes.A), codes.ConvertChecked<float>());
        }
    }

    /// <summary>Default is transparent black, while the RGB constructor supplies opaque alpha.</summary>
    [Fact]
    public void ConstructorsAndWithAlphaPreserveCodeSemantics()
    {
        Assert.Equal(new SRgba<byte>(0, 0, 0, 0), default(SRgba8).ToCodes());
        Assert.Equal(new SRgba<byte>(0, 0, 0, 255), new SRgba8(0, 0, 0).ToCodes());
        SRgba8 original = new(13, 127, 231);
        Assert.Equal(new SRgba<byte>(13, 127, 231, 0), original.WithAlpha(0).ToCodes());
        Assert.Equal((byte)255, original.A);
        Assert.Equal(new SRgba8(13, 127, 231, 0), original.WithAlpha(0));
    }

    /// <summary>Normalization never decodes gamma or associates alpha, even when RGB is hidden.</summary>
    [Fact]
    public void HiddenRgbIsNeitherGammaDecodedNorPremultiplied()
    {
        SRgba8 input = new(128, 64, 231, 0);
        SRgba<float> actual = input;
        AssertBits(new SRgba<float>(128 / 255f, 64 / 255f, 231 / 255f, 0), actual);
        Assert.NotEqual(0f, actual.R);
        Assert.NotEqual(actual.ToLinear<float>().R, actual.R);
        Assert.Equal(input, (SRgba8)actual);
        SRgba8 translucent = input.WithAlpha(128);
        SRgba<float> straight = translucent;
        Assert.Equal(actual.R, straight.R);
        Assert.Equal(128 / 255f, straight.A);
    }

    /// <summary>Exact decimal midpoint inputs exercise nearest-even on either parity in each channel.</summary>
    [Fact]
    public void DecimalMidpointsUseExactNearestEven()
    {
        // These terminating decimals have products with 255 of 25.5, 76.5, 127.5, 178.5, 229.5.
        decimal[] midpoints = [0.1m, 0.3m, 0.5m, 0.7m, 0.9m];
        byte[] expected = [26, 76, 128, 178, 230];
        for (int component = 0; component < 4; component++)
        {
            for (int index = 0; index < midpoints.Length; index++)
            {
                SRgba<decimal> input = InNormalizedComponent(component, midpoints[index]);
                Assert.Equal(InComponent(component, expected[index]), SRgba8.FromNormalized(input));
            }
        }
    }

    /// <summary>All quantization boundaries test their float neighbors with an independent double product.</summary>
    [Fact]
    public void FloatNeighborsAtEveryQuantizationBoundaryAreStrict()
    {
        for (int lower = 0; lower < 255; lower++)
        {
            float midpoint = (lower + 0.5f) / 255f;
            float[] neighbors = [float.BitDecrement(midpoint), midpoint, float.BitIncrement(midpoint)];
            foreach (float value in neighbors)
            {
                // A binary32 significand times 255 is exact in binary64, so this oracle does not round the product early.
                byte expected = checked((byte)Math.Round((double)value * 255, MidpointRounding.ToEven));
                for (int component = 0; component < 4; component++)
                {
                    SRgba<float> input = InNormalizedComponent(component, value);
                    Assert.Equal(InComponent(component, expected), (SRgba8)input);
                    Assert.Equal(InComponent(component, expected), SRgba8.FromNormalized(input));
                }
            }
        }
    }

    /// <summary>Generic raw storage remains HDR-capable, but semantic reverse conversion rejects every out-of-unit channel.</summary>
    [Fact]
    public void ReverseConversionRejectsOutOfUnitRatherThanClamping()
    {
        float[] invalid = [-1, -float.Epsilon, float.BitIncrement(1), 2, float.MaxValue];
        foreach (float value in invalid)
        {
            for (int component = 0; component < 4; component++)
            {
                SRgba<float> raw = InNormalizedComponent(component, value);
                Assert.Equal(raw, raw.ConvertChecked<float>());
                Assert.Throws<ArgumentOutOfRangeException>(() => (SRgba8)raw);
                Assert.Throws<ArgumentOutOfRangeException>(() => SRgba8.FromNormalized(raw));
            }
        }

        SRgba<float> endpoints = new(-0f, 1, 0, 1);
        Assert.Equal(new SRgba8(0, 255, 0, 255), (SRgba8)endpoints);
    }

    /// <summary>Named generic normalization preserves exact numeric rules without adding generic implicit conversions.</summary>
    [Fact]
    public void NamedGenericConversionsRetainFractionAndRangeValidation()
    {
        SRgba8 input = new(1, 127, 254, 128);
        Assert.Equal(input.ToCodes().NormalizeUnorm<double>(255), input.ToNormalized<double>());
        Assert.Equal(input.ToCodes().NormalizeUnorm<decimal>(255), input.ToNormalized<decimal>());
        Assert.Equal(input.ToCodes().NormalizeUnorm<Half>(255), input.ToNormalized<Half>());
        Assert.Throws<ArithmeticException>(() => input.ToNormalized<int>());
        Assert.Throws<ArithmeticException>(() => input.ToNormalized<BigInteger>());
        Assert.Equal(new SRgba<int>(1, 0, 1, 1), new SRgba8(255, 0, 255).ToNormalized<int>());
        Assert.Equal(new SRgba8(255, 0, 255), SRgba8.FromNormalized(new SRgba<int>(1, 0, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SRgba8.FromNormalized(new SRgba<int>(255, 0, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SRgba8.FromNormalized(new SRgba<decimal>(0, 0, 0, 1.0000000000000000000000000001m)));
    }

    private static SRgba8 InComponent(int component, byte value) => new(
        component == 0 ? value : (byte)0,
        component == 1 ? value : (byte)0,
        component == 2 ? value : (byte)0,
        component == 3 ? value : (byte)0);

    private static SRgba<T> InNormalizedComponent<T>(int component, T value)
        where T : INumber<T> => new(
            component == 0 ? value : T.Zero,
            component == 1 ? value : T.Zero,
            component == 2 ? value : T.Zero,
            component == 3 ? value : T.Zero);

    private static void AssertBits(SRgba<float> expected, SRgba<float> actual)
    {
        Assert.Equal(BitConverter.SingleToInt32Bits(expected.R), BitConverter.SingleToInt32Bits(actual.R));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected.G), BitConverter.SingleToInt32Bits(actual.G));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected.B), BitConverter.SingleToInt32Bits(actual.B));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected.A), BitConverter.SingleToInt32Bits(actual.A));
    }
}
