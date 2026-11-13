using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Verifies transfer precision, legacy compatibility, and linear operations.</summary>
public sealed class ColorOperationsTests
{
    /// <summary>Paint and clear retain their original float and mixed-double expressions.</summary>
    [Fact]
    public void DecodeMatchesOriginalPaintAndClear()
    {
        float[] values = [0, float.Epsilon, MathF.BitDecrement(0.04045f), 0.04045f, MathF.BitIncrement(0.04045f), 0.1f, 0.5f, 1];
        foreach (float c in values)
        {
            var input = new SrgbRgba<float>(c, c, c, 0.9f);
            float paint = (c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f)) * input.A;
            double clear = (c <= 0.04045f ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)) * input.A;
            Assert.Equal(paint, input.ToLinear<float>().PremultiplyLinear<float>().R);
            Assert.Equal(clear, input.ToLinear<double>().PremultiplyLinear<double>().R);
        }
    }

    /// <summary>Legacy subtraction, endpoint identity, zero alpha, and float-valid extrapolation are retained.</summary>
    [Fact]
    public void RawRetainsLegacyMixedPrecision()
    {
        Assert.Equal(0.49999997f, Color.InterpolateSrgbLinearPremultipliedRaw(new(0, 0, 0, 0.1f), new(0, 0, 0, 0.9f), 0.5f).W);
        Assert.Equal(1f, Color.InterpolateSrgbLinearPremultipliedRaw(new(0, 0, 0, 0.9f), new(0, 0, 0, 1), MathF.BitIncrement(1)).W);
        Vector4 hidden = new(1, 0.5f, 0.25f, 0);
        Assert.Equal(hidden, Color.InterpolateSrgbLinearPremultipliedRaw(hidden, default, 0));
        Assert.Equal(default, Color.InterpolateSrgbLinearPremultipliedRaw(hidden, default, 0.5f));
        Assert.True(float.IsNaN(Color.InterpolateSrgbLinearPremultipliedRaw(default, Vector4.One, float.NaN).W));
    }

    /// <summary>Linear operations retain HDR and reject fractional integral results.</summary>
    [Fact]
    public void LinearAlphaAndCompositingPoliciesAreExplicit()
    {
        var source = new LinearRgba<decimal>(4, -2, 1, 0.5m);
        Assert.Equal(new LinearRgba<decimal>(2, -1, 0.5m, 0.5m), source.PremultiplyLinear<decimal>());
        Assert.Equal(new LinearRgba<decimal>(0, 0, 0, 0), source.WithAlpha(0).UnpremultiplyLinear<decimal>());
        Assert.Equal(new LinearRgba<decimal>(6, -3, 1.5m, 0.75m), source.SourceOverLinearPremultiplied(source));
        Assert.Equal(new LinearRgba<decimal>(8, -4, 2, 1), source.AddLinearPremultiplied(source));
        Assert.Equal(new LinearRgba<decimal>(1, 0, 1, 1), source.AddUnitLinearPremultiplied(source));
        Assert.Throws<ArithmeticException>(() => source.PremultiplyLinear<int>());
        Assert.Throws<ArgumentOutOfRangeException>(() => source.WithAlpha(2).PremultiplyLinear<double>());
    }

    /// <summary>Matrix bias precedes alpha clipping and RGB reassociation.</summary>
    [Fact]
    public void MatrixAndGrayscaleRespectStraightLinearSemantics()
    {
        var color = new LinearRgba<float>(2, -1, 0.5f, 0.5f);
        Assert.Equal(new LinearRgba<float>(5, -2, 1, 1), color.ApplyMatrixLinearPremultiplied(Matrix4x4.Identity, new(1, 0, 0, 1)));
        Assert.Equal(0.2126m, new LinearRgba<int>(1, 0, 0, 1).ToLinearGrayscale<decimal>().R);
        Assert.Throws<ArithmeticException>(() => new LinearRgba<int>(1, 0, 0, 1).ToLinearGrayscale<int>());
    }

    /// <summary>Strict interpolation validates inputs and keeps transparent endpoints.</summary>
    [Fact]
    public void StrictInterpolationKeepsEndpointsAndTinyConstants()
    {
        var hidden = new SrgbRgba<double>(0.7, 0.3, 0.1, 0);
        Assert.Equal(hidden, hidden.InterpolateSrgbLinearPremultiplied(default, 0d));
        Assert.Equal(default, hidden.InterpolateSrgbLinearPremultiplied(default, 0.5d));
        var tiny = new SrgbRgba<double>(double.Epsilon, double.Epsilon, double.Epsilon, double.Epsilon);
        Assert.Equal(tiny, tiny.InterpolateSrgbLinearPremultiplied(tiny, 0.5d));
        Assert.Equal(double.Epsilon, tiny.WithAlpha(0.25d).InterpolateSrgbLinearPremultiplied(tiny.WithAlpha(0.75d), 0.5d).R);
        Assert.Throws<ArgumentOutOfRangeException>(() => hidden.InterpolateSrgbLinearPremultiplied(default, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SrgbRgba<double>(2, 0, 0, 1).InterpolateSrgbLinearPremultiplied(default, 0d));
    }
}
