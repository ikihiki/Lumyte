using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Protects same-precision IEEE alpha arithmetic and hot-path allocations.</summary>
public sealed class ColorIeeeFastPathTests
{
    /// <summary>IEEE alpha multiplication has native rounding, signed zero, and finite overflow rejection.</summary>
    [Fact]
    public void NativeAlphaProductsRetainIeeeSemantics()
    {
        Check<float>();
        Check<double>();
        Check<Half>();
        LinearRgba<int> quantized = new LinearRgba<float>(0.3f, 0, 0, 1).QuantizeUnorm(255);
        Assert.Equal(77, quantized.R);
        byte[] pixel = new byte[4];
        new LinearRgba<float>(0.3f, 0, 0, 1).WritePixel(pixel, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Assert.Equal(77, pixel[0]);
        Assert.Throws<OverflowException>(() => new LinearRgba<double>(double.MaxValue, 0, 0, 0.5).UnpremultiplyLinear<double>());
        Assert.Equal(new LinearRgba<decimal>(0.15m, 0, 0, 0.5m), new LinearRgba<decimal>(0.3m, 0, 0, 0.5m).PremultiplyLinear<decimal>());
        Assert.Throws<ArithmeticException>(() => new LinearRgba<decimal>(0.3m, 0, 0, 0.5m).PremultiplyLinear<int>());
    }

    /// <summary>Migrated float paint and float-input/double clear must allocate no managed storage per color.</summary>
    [Fact]
    public void ProductionTransferAndAssociationDoNotAllocate()
    {
        var input = new SrgbRgba<float>(0.3f, 0.02f, 0.8f, 0.7f);
        float paint = 0;
        double clear = 0;
        for (int i = 0; i < 1000; i++)
        {
            paint += input.ToLinear<float>().PremultiplyLinear<float>().R;
            clear += input.ToLinear<double>().PremultiplyLinear<double>().R;
        }

        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            paint += input.ToLinear<float>().PremultiplyLinear<float>().R;
            clear += input.ToLinear<double>().PremultiplyLinear<double>().R;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, allocated);
        Assert.True(paint > 0 && clear > 0);
    }

    private static void Check<T>()
        where T : IFloatingPointIeee754<T>
    {
        T negativeZero = -T.Zero;
        T alpha = T.CreateChecked(0.5);
        var input = new LinearRgba<T>(negativeZero, T.Epsilon, T.CreateChecked(0.3), alpha);
        LinearRgba<T> associated = input.PremultiplyLinear<T>();
        Assert.True(T.IsNegative(associated.R));
        Assert.Equal(input.G * alpha, associated.G);
        Assert.Equal(input.B * alpha, associated.B);
        LinearRgba<T> straight = input.UnpremultiplyLinear<T>();
        Assert.True(T.IsNegative(straight.R));
        Assert.Equal(input.G / alpha, straight.G);
        Assert.Equal(input.B / alpha, straight.B);
    }
}
