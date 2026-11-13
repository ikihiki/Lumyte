using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Independent baseline formulas and strict color-operation regression probes.</summary>
public sealed class ColorIndependentIntegrationReviewTests
{
    /// <summary>Paint and both clear adapters preserve the baseline operation sequence bit for bit.</summary>
    [Fact]
    public void ProductionPaintAndClearAdapterExpressionsMatchBaselineBits()
    {
        var random = new Random(61258);
        for (int i = 0; i < 4096; i++)
        {
            float channel = random.NextSingle();
            float alpha = random.NextSingle();
            var input = new SrgbRgba<float>(channel, channel, channel, alpha);
            float paint = BaselinePaint(channel) * alpha;
            double clear = BaselineDecode(channel) * alpha;
            Assert.Equal(BitConverter.SingleToInt32Bits(paint), BitConverter.SingleToInt32Bits(input.ToLinear<float>().PremultiplyLinear<float>().R));
            Assert.Equal(BitConverter.DoubleToInt64Bits(clear), BitConverter.DoubleToInt64Bits(input.ToLinear<double>().PremultiplyLinear<double>().R));
        }
    }

    /// <summary>The UI raw adapter preserves mixed-precision arithmetic.</summary>
    [Fact]
    public void UiRawMatchesIndependentBaseline()
    {
        var random = new Random(61258);
        for (int i = 0; i < 1024; i++)
        {
            Vector4 from = new(random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle());
            Vector4 to = new(random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle());
            float amount = (random.NextSingle() * 3) - 1;
            Assert.Equal(BaselineUi(from, to, amount), Color.InterpolateSrgbLinearPremultipliedRaw(from, to, amount));
        }
    }

    /// <summary>Equal nonzero subnormal RGB must not disappear when only endpoint alpha differs.</summary>
    [Fact]
    public void StrictInterpolationRetainsSubnormalConstantRgbAcrossDifferentAlphas()
    {
        var first = new SrgbRgba<double>(double.Epsilon, double.Epsilon, double.Epsilon, 0.25);
        SrgbRgba<double> last = first.WithAlpha(0.75);
        Assert.Equal(new SrgbRgba<double>(double.Epsilon, double.Epsilon, double.Epsilon, 0.5), first.InterpolateSrgbLinearPremultiplied(last, 0.5));
    }

    /// <summary>Different subnormal channels retain their exact low-segment interpolation before final rounding.</summary>
    [Fact]
    public void StrictInterpolationRetainsDifferentSubnormalRgb()
    {
        var first = new SrgbRgba<double>(double.Epsilon, 0, 0, 1);
        var last = new SrgbRgba<double>(2 * double.Epsilon, 0, 0, 1);
        Assert.Equal(2 * double.Epsilon, first.InterpolateSrgbLinearPremultiplied(last, 0.5).R);
    }

    /// <summary>Strict interpolation rejects mathematically negative alpha before rounding to signed zero.</summary>
    [Fact]
    public void StrictInterpolationRejectsNegativeAlphaThatRoundsToZero()
    {
        var first = new SrgbRgba<double>(0, 0, 0, 0);
        var last = new SrgbRgba<double>(0, 0, 0, double.Epsilon);
        Assert.Throws<ArgumentOutOfRangeException>(() => first.InterpolateSrgbLinearPremultiplied(last, -0.25));
    }

    /// <summary>Native double decode preserves the native threshold boundary.</summary>
    [Fact]
    public void DoubleDecodeAtNativeThresholdMatchesStatedPiecewiseFormula()
    {
        const double Channel = 0.04045;
        double expected = Channel / 12.92;
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(new SrgbRgba<double>(Channel, Channel, Channel, 1).ToLinear<double>().R));
    }

    /// <summary>Toe cancellation is not valid when extrapolation leaves the linear transfer segment.</summary>
    [Fact]
    public void ToeExtrapolationUsesNonlinearEncodeOutsideToe()
    {
        var first = new SrgbRgba<double>(0.01, 0, 0, 1);
        var last = new SrgbRgba<double>(0.02, 0, 0, 1);
        double linear = (0.01 / 12.92) + (((0.02 / 12.92) - (0.01 / 12.92)) * 10);
        double expected = (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
        Assert.Equal(expected, first.InterpolateSrgbLinearPremultiplied(last, 10d).R, 14);
    }

    /// <summary>Eligible exact toe cancellation precedes rounded decoded unit validation.</summary>
    [Fact]
    public void SubnormalToeExtrapolationDoesNotValidateRoundedPipelineFirst()
    {
        var first = new SrgbRgba<double>(6 * double.Epsilon, 0, 0, 1);
        var last = new SrgbRgba<double>(7 * double.Epsilon, 0, 0, 1);
        Assert.Equal(5 * double.Epsilon, first.InterpolateSrgbLinearPremultiplied(last, -1d).R);
    }

    private static float BaselinePaint(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static double BaselineDecode(float value) => value <= 0.04045f ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static float BaselineEncode(double value) => (float)(value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055);

    private static Vector4 BaselineUi(Vector4 from, Vector4 to, float amount)
    {
        if (amount == 0)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        double alpha = from.W + ((to.W - from.W) * (double)amount);
        if (alpha == 0)
        {
            return default;
        }

        float Channel(float first, float last) => BaselineEncode(((BaselineDecode(first) * from.W) + (((BaselineDecode(last) * to.W) - (BaselineDecode(first) * from.W)) * amount)) / alpha);
        return new(Channel(from.X, to.X), Channel(from.Y, to.Y), Channel(from.Z, to.Z), (float)alpha);
    }
}
