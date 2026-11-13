using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Protects same-precision IEEE alpha arithmetic and rounding.</summary>
public sealed class ColorIeeeFastPathTests
{
    /// <summary>IEEE alpha multiplication has native rounding, signed zero, and finite overflow rejection.</summary>
    [Fact]
    public void NativeAlphaProductsRetainIeeeSemantics()
    {
        Check<float>();
        Check<double>();
        Check<Half>();
        LRgba<int> quantized = new LRgba<float>(0.3f, 0, 0, 1).QuantizeUnorm(255);
        Assert.Equal(77, quantized.R);
        byte[] pixel = new byte[4];
        new LRgba<float>(0.3f, 0, 0, 1).WritePixel(pixel, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Assert.Equal(77, pixel[0]);
        Assert.Throws<OverflowException>(() => new LRgba<double>(double.MaxValue, 0, 0, 0.5).UnpremultiplyLinear<double>());
        Assert.Equal(new LRgba<decimal>(0.15m, 0, 0, 0.5m), new LRgba<decimal>(0.3m, 0, 0, 0.5m).PremultiplyLinear<decimal>());
        Assert.Throws<ArithmeticException>(() => new LRgba<decimal>(0.3m, 0, 0, 0.5m).PremultiplyLinear<int>());
    }

    private static void Check<T>()
        where T : IFloatingPointIeee754<T>
    {
        T negativeZero = -T.Zero;
        T alpha = T.CreateChecked(0.5);
        var input = new LRgba<T>(negativeZero, T.Epsilon, T.CreateChecked(0.3), alpha);
        LRgba<T> associated = input.PremultiplyLinear<T>();
        Assert.True(T.IsNegative(associated.R));
        Assert.Equal(input.G * alpha, associated.G);
        Assert.Equal(input.B * alpha, associated.B);
        LRgba<T> straight = input.UnpremultiplyLinear<T>();
        Assert.True(T.IsNegative(straight.R));
        Assert.Equal(input.G / alpha, straight.G);
        Assert.Equal(input.B / alpha, straight.B);
    }
}
