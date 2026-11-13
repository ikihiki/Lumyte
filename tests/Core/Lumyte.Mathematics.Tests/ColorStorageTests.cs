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
        Assert.Equal(255f, SRgba<float>.FromArgb32Codes(0xFFFFFFFF).R);
        Assert.Equal(new SRgba<int>(-3, 400, 0, 255), new SRgba<int>(-3, 400, 0, 255));
        Assert.Equal(default, new SRgba<float>(0, -0f, 0, 0));
        Assert.Equal(default(SRgba<float>).GetHashCode(), new SRgba<float>(-0f, 0, 0, 0).GetHashCode());
        Assert.False(SRgba<double>.TryCreate(double.NaN, 0, 0, 0, out SRgba<double> invalid));
        Assert.Equal(default, invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SRgba<double>(0, 0, 0, double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => SRgba<int>.FromComponents(new[] { 1, 2, 3 }));
        int[] untouched = [7, 7, 7];
        Assert.Throws<ArgumentException>(() => new SRgba<int>(1, 2, 3, 4).CopyTo(untouched));
        Assert.Equal(new[] { 7, 7, 7 }, untouched);
        Assert.Equal(new SRgba<int>(1, 2, 3, 9), new SRgba<int>(1, 2, 3, 4).WithAlpha(9));
    }

    /// <summary>Huge maxima never pass through a prematurely rounded floating upper bound.</summary>
    [Fact]
    public void HugeMaximumEndpointsAndMidpoints()
    {
        var huge = BigInteger.Pow(10, 400);
        Assert.Equal(new SRgba<double>(0, 0.5, 1, 1), new SRgba<BigInteger>(0, huge / 2, huge, huge).NormalizeUnorm<double>(huge));
        Assert.Equal(new SRgba<BigInteger>(0, huge / 2, huge, huge), new SRgba<double>(0, 0.5, 1, 1).QuantizeUnorm(huge));
        Assert.Equal(ulong.MaxValue, new SRgba<double>(1, 1, 1, 1).QuantizeUnorm(ulong.MaxValue).A);
        Assert.Equal(9223372036854775808UL, new SRgba<double>(0.5, 0.5, 0.5, 0.5).QuantizeUnorm(ulong.MaxValue).R);
        Assert.Equal(new SRgba<int>(0, 2, 2, 4), new SRgba<decimal>(0.125m, 0.375m, 0.625m, 0.875m).QuantizeUnorm(4));
        Assert.Equal(0, new SRgba<double>(double.BitDecrement(0.125), 0, 0, 0).QuantizeUnorm(4).R);
        Assert.Equal(1, new SRgba<double>(double.BitIncrement(0.125), 0, 0, 0).QuantizeUnorm(4).R);
        Assert.Throws<ArithmeticException>(() => new SRgba<int>(1, 0, 0, 0).NormalizeUnorm<int>(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SRgba<double>(1.5, 0, 0, 0).NormalizeUnorm<double>(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SRgba<int>(-1, 0, 0, 0).NormalizeUnorm<double>(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SRgba<double>(1.01, 0, 0, 0).QuantizeUnorm(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SRgba<double>(1.5, 0, 0, 0).ToArgb32Codes());
    }

    /// <summary>A huge reference-backed numeric storage is not mistaken for unmanaged SIMD data.</summary>
    [Fact]
    public void BigIntegerStorageContainsReferences()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SRgba<BigInteger>>());
        var huge = BigInteger.Pow(10, 400);
        var value = new SRgba<BigInteger>(huge, -huge, huge, huge);
        var destination = new SRgba<BigInteger>[1];
        destination[0] = value;
        Assert.Equal(value, destination[0]);
        Assert.Throws<OverflowException>(() => value.ConvertChecked<double>());
    }

    private static void Check<T>()
        where T : INumber<T>
    {
        var codes = SRgba<T>.FromRgba8Codes(1, 2, 3, 4);
        Assert.Equal(T.CreateChecked(3), codes.B);
        Assert.Equal(0x04010203u, codes.ToArgb32Codes());
        Assert.Equal(new SRgba<double>(0.25, 0.5, 0.75, 1), codes.NormalizeUnorm<double>(T.CreateChecked(4)));
        Assert.Equal(new SRgba<int>(1, 2, 3, 4), codes.ConvertChecked<int>());
    }
}
