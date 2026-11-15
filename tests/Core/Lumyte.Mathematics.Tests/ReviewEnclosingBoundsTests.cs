using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Regression coverage for outward-rounded rectangle enclosures.</summary>
public sealed class ReviewEnclosingBoundsTests
{
    /// <summary>Factories and unions must retain the requested upper corner.</summary>
    [Fact]
    public void FloatingFactoriesEncloseBothCorners()
    {
        Check<double>(-1, 0.2);
        Check<float>(-1, 0.1f);
        Check<Half>((Half)(-1), (Half)0.2);
        Check<double>(-1e300, 1);
        Check<float>(-1e30f, 1);
        Check<Half>((Half)(-32768), (Half)1);
    }

    /// <summary>Decimal cancellation also requires the next representable extent.</summary>
    [Fact]
    public void DecimalFactoriesEncloseBothCorners()
    {
        Check<decimal>(-1, 0.0000000000000000000000000001m);
        Check<decimal>(-10000000000000000000000000000m, 0.1m);
    }

    /// <summary>The first extent successor repairs cancellation without arbitrary padding.</summary>
    [Fact]
    public void RoundingIsMinimalAndOutsetRetainsItsUpperEdge()
    {
        var bounds = Rect2D<double>.FromMinMax(new(-1, -1), new(0.2, 0.2));
        Assert.Equal(double.BitIncrement(1.2), bounds.Width);
        Assert.True(-1 + double.BitDecrement(bounds.Width) < 0.2);
        var decimalBounds = Rect2D<decimal>.FromMinMax(new(-10000000000000000000000000000m, 0), new(0.1m, 0));
        Assert.Equal(10000000000000000000000000001m, decimalBounds.Width);
        var original = new Rect2D<double>(-1e300, -1e300, 1e300, 1e300);
        Rect2D<double> expanded = original.Outset(1, 1);
        Assert.True(expanded.Contains(new(1, 1)));
        Assert.Equal(new Rect2D<int>(-1, -1, 4, 4), new Rect2D<int>(0, 0, 2, 2).Outset(1, 1));
    }

    /// <summary>Exact bounds, signed zero, and subnormal extents stay unchanged.</summary>
    [Fact]
    public void ExactBoundsAreNotExpanded()
    {
        Assert.Equal(2d, Rect2D<double>.FromMinMax(new(-1, -1), new(1, 1)).Width);
        Assert.Equal(double.Epsilon, Rect2D<double>.FromMinMax(default, new(double.Epsilon, double.Epsilon)).Width);
        Assert.Equal(float.Epsilon, Rect2D<float>.FromMinMax(default, new(float.Epsilon, float.Epsilon)).Width);
        Assert.Equal(Half.Epsilon, Rect2D<Half>.FromMinMax(default, new(Half.Epsilon, Half.Epsilon)).Width);
        var zero = Rect2D<double>.FromMinMax(new(-0d, -0d), new(0d, 0d));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(zero.X));
        Assert.Equal(0d, zero.Width);
        Check<int>(-1, 2);
        Check<uint>(1, 3);
        Check<BigInteger>(-1, BigInteger.One << 200);
    }

    /// <summary>Invalid ordering and unrepresentable finite bounds fail predictably.</summary>
    [Fact]
    public void InvalidOrUnrepresentableBoundsFail()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rect2D<uint>.FromMinMax(new(2, 2), new(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rect2D<int>.FromMinMax(new(int.MaxValue, 0), new(int.MinValue, 0)));
        Assert.Throws<OverflowException>(() => Rect2D<int>.FromMinMax(new(int.MinValue, 0), new(int.MaxValue, 0)));
        Assert.Throws<OverflowException>(() => Rect2D<double>.FromMinMax(new(-double.MaxValue, 0), new(double.MaxValue, 0)));
        Assert.False(Rect2D<double>.TryFromPoints([new(-double.MaxValue, 0), new(double.MaxValue, 0)], out _));
        Assert.False(Rect2D<double>.TryCreate(double.MaxValue, 0, double.MaxValue, 0, out _));
        Assert.Throws<OverflowException>(() => Rect2D<double>.FromMinMax(new(-double.MaxValue, 0), new(1, 0)));
        Assert.Throws<OverflowException>(() => Rect2D<float>.FromMinMax(new(-float.MaxValue, 0), new(1, 0)));
        Assert.Throws<OverflowException>(() => Rect2D<Half>.FromMinMax(new(-Half.MaxValue, (Half)0), new((Half)1, (Half)0)));
        Assert.Throws<OverflowException>(() => Rect2D<decimal>.FromMinMax(new(-decimal.MaxValue, 0), new(0.1m, 0)));
    }

    /// <summary>A faithful managed decimal wrapper uses verified decimal rounding.</summary>
    [Fact]
    public void ManagedDecimalFactoriesEncloseBothCorners()
    {
        // Reuse the existing private numeric fixture without widening production APIs.
        Type scalar = typeof(CustomNumberGeometryTests).GetNestedType("ManagedDecimal", System.Reflection.BindingFlags.NonPublic)!;
        System.Reflection.MethodInfo method = typeof(ReviewEnclosingBoundsTests).GetMethod(nameof(CheckDecimalWrapper), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        method.MakeGenericMethod(scalar).Invoke(null, null);
    }

    private static void CheckDecimalWrapper<T>()
        where T : INumber<T> => Check(T.CreateChecked(-10000000000000000000000000000m), T.CreateChecked(0.1m));

    private static void Check<T>(T min, T max)
        where T : INumber<T>
    {
        var lower = new Point2D<T>(min, min);
        var upper = new Point2D<T>(max, max);
        var bounds = Rect2D<T>.FromMinMax(lower, upper);
        Assert.True(bounds.Contains(lower));
        Assert.True(bounds.Contains(upper));
        Assert.Equal(bounds, Rect2D<T>.FromPoints([upper, lower, upper]));
        Assert.True(Rect2D<T>.TryFromPoints([lower, upper], out Rect2D<T> attempted));
        Assert.Equal(bounds, attempted);
        var first = new Rect2D<T>(min, min, T.Zero, T.Zero);
        var second = new Rect2D<T>(max, max, T.Zero, T.Zero);
        Assert.Equal(bounds, first.Union(second));
        Assert.Equal(bounds, second.Union(first));
    }
}
