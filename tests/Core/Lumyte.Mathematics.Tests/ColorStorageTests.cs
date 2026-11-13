using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Verifies untagged storage and exact integer-code normalization.</summary>
public sealed class ColorStorageTests
{
    /// <summary>All seventeen BCL real numeric types store the same raw codes.</summary>
    [Fact]
    public void AllBuiltInNumericStorageTypes()
    {
        Check<byte>();
        Check<sbyte>();
        Check<short>();
        Check<ushort>();
        Check<int>();
        Check<uint>();
        Check<long>();
        Check<ulong>();
        Check<nint>();
        Check<nuint>();
        Check<Int128>();
        Check<UInt128>();
        Check<BigInteger>();
        Check<decimal>();
        Check<Half>();
        Check<float>();
        Check<double>();
    }

    /// <summary>Storage is finite but imposes no unit constraint on RGB or alpha.</summary>
    [Fact]
    public void RawStorageAndFiniteValidation()
    {
        Assert.Equal(255f, Rgba<float>.FromArgb32Codes(0xFFFFFFFF).R);
        Assert.Equal(new Rgba<int>(-3, 400, 0, 255), new Rgba<int>(-3, 400, 0, 255));
        Assert.Equal(default, new Rgba<float>(0, -0f, 0, 0));
        Assert.Equal(default(Rgba<float>).GetHashCode(), new Rgba<float>(-0f, 0, 0, 0).GetHashCode());
        Assert.False(Rgba<double>.TryCreate(double.NaN, 0, 0, 0, out Rgba<double> invalid));
        Assert.Equal(default, invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rgba<double>(0, 0, 0, double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => Rgba<int>.FromComponents(new[] { 1, 2, 3 }));
        int[] untouched = [7, 7, 7];
        Assert.Throws<ArgumentException>(() => new Rgba<int>(1, 2, 3, 4).CopyTo(untouched));
        Assert.Equal(new[] { 7, 7, 7 }, untouched);
        Assert.Equal(new Rgba<int>(1, 2, 3, 9), new Rgba<int>(1, 2, 3, 4).WithAlpha(9));
    }

    /// <summary>Huge maxima never pass through a prematurely rounded floating upper bound.</summary>
    [Fact]
    public void HugeMaximumEndpointsAndMidpoints()
    {
        var huge = BigInteger.Pow(10, 400);
        Assert.Equal(new Rgba<double>(0, 0.5, 1, 1), new Rgba<BigInteger>(0, huge / 2, huge, huge).NormalizeUnorm<double>(huge));
        Assert.Equal(new Rgba<BigInteger>(0, huge / 2, huge, huge), new Rgba<double>(0, 0.5, 1, 1).QuantizeUnorm(huge));
        Assert.Equal(ulong.MaxValue, new Rgba<double>(1, 1, 1, 1).QuantizeUnorm(ulong.MaxValue).A);
        Assert.Equal(9223372036854775808UL, new Rgba<double>(0.5, 0.5, 0.5, 0.5).QuantizeUnorm(ulong.MaxValue).R);
        Assert.Equal(new Rgba<int>(0, 2, 2, 4), new Rgba<decimal>(0.125m, 0.375m, 0.625m, 0.875m).QuantizeUnorm(4));
        Assert.Equal(0, new Rgba<double>(double.BitDecrement(0.125), 0, 0, 0).QuantizeUnorm(4).R);
        Assert.Equal(1, new Rgba<double>(double.BitIncrement(0.125), 0, 0, 0).QuantizeUnorm(4).R);
        Assert.Throws<ArithmeticException>(() => new Rgba<int>(1, 0, 0, 0).NormalizeUnorm<int>(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rgba<double>(1.5, 0, 0, 0).NormalizeUnorm<double>(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rgba<int>(-1, 0, 0, 0).NormalizeUnorm<double>(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rgba<double>(1.01, 0, 0, 0).QuantizeUnorm(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rgba<double>(1.5, 0, 0, 0).ToArgb32Codes());
    }

    /// <summary>A huge reference-backed numeric storage is not mistaken for unmanaged SIMD data.</summary>
    [Fact]
    public void BigIntegerStorageContainsReferences()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<Rgba<BigInteger>>());
        var huge = BigInteger.Pow(10, 400);
        var value = new Rgba<BigInteger>(huge, -huge, huge, huge);
        var destination = new Rgba<BigInteger>[1];
        destination[0] = value;
        Assert.Equal(value, destination[0]);
        Assert.Throws<OverflowException>(() => value.ConvertChecked<double>());
    }

    private static void Check<T>()
        where T : INumber<T>
    {
        var codes = Rgba<T>.FromRgba8Codes(1, 2, 3, 4);
        Assert.Equal(T.CreateChecked(3), codes.B);
        Assert.Equal(0x04010203u, codes.ToArgb32Codes());
        Assert.Equal(new Rgba<double>(0.25, 0.5, 0.75, 1), codes.NormalizeUnorm<double>(T.CreateChecked(4)));
        Assert.Equal(new Rgba<int>(1, 2, 3, 4), codes.ConvertChecked<int>());
    }
}
