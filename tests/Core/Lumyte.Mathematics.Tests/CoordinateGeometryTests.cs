using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks raw edge arithmetic independently of domain empty policies.</summary>
public sealed class CoordinateGeometryTests
{
    /// <summary>Unsigned disjoint intersections clamp before subtraction and edge arithmetic never wraps.</summary>
    [Fact]
    public void UnsignedCoordinateArithmeticDoesNotWrap()
    {
        Assert.Equal((10u, 10u, 0u, 0u), CoordinateGeometry.Intersect((10u, 10u, 2u, 2u), (0u, 0u, 2u, 2u)));
        Assert.Throws<OverflowException>(() => CoordinateGeometry.ContainsHalfOpen(uint.MaxValue, 0u, 1u, 1u, uint.MaxValue, 0u));
        Assert.Equal(0u, new Bounds2D<uint>(10u, 10u, 0u, 0u).Width);
    }

    /// <summary>Raw edges match the original arithmetic including signed zero, NaN and infinity.</summary>
    [Fact]
    public void RawEdgesMatchIeeeOracle()
    {
        double[] values = [double.NegativeInfinity, -1, -0d, 0d, 1, double.PositiveInfinity, double.NaN];
        foreach (double a in values)
        {
            foreach (double b in values)
            {
                (double Left, double Top, double Right, double Bottom) first = (a, b, a, b);
                (double Left, double Top, double Right, double Bottom) second = (b, a, b, a);
                (double Left, double Top, double Right, double Bottom) intersection = CoordinateGeometry.IntersectRawEdges(first, second);
                (double Left, double Top, double Right, double Bottom) union = CoordinateGeometry.EncloseRawEdges(first, second);
                AssertBits((Math.Max(a, b), Math.Max(b, a), Math.Min(a, b), Math.Min(b, a)), intersection);
                AssertBits((Math.Min(a, b), Math.Min(b, a), Math.Max(a, b), Math.Max(b, a)), union);
                var firstBounds = new Bounds2D<double>(a, b, a, b);
                var secondBounds = new Bounds2D<double>(b, a, b, a);
                Bounds2D<double> bounds = firstBounds.Intersect(secondBounds);
                AssertBits(intersection, (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
            }
        }
    }

    /// <summary>Half-open boundaries retain the intentional IEEE negative-infinity inclusion.</summary>
    [Fact]
    public void HalfOpenEdgesRetainIeeeBoundaries()
    {
        Assert.True(Bounds2D<double>.Unbounded.Contains(double.NegativeInfinity, double.NegativeInfinity));
        Assert.False(Bounds2D<double>.Unbounded.Contains(double.PositiveInfinity, 0));
        Assert.False(Bounds2D<double>.Unbounded.Contains(0, double.NaN));
        Assert.True(CoordinateGeometry.ContainsHalfOpenEdges((-0d, -0d, 1d, 1d), 0d, -0d));
        Assert.False(CoordinateGeometry.ContainsHalfOpenEdges((0d, 0d, -0d, 1d), -0d, 0d));
        var rectangle = new Rect2D<double>(-0d, -0d, 1, 1);
        Assert.True(rectangle.ContainsHalfOpen(new(0, 0)));
        Assert.False(rectangle.ContainsHalfOpen(new(1, 0)));
        Assert.Equal((20d, 20d, 10d, 10d), CoordinateGeometry.IntersectRawEdges((0d, 0d, 10d, 10d), (20d, 20d, 30d, 30d)));
        Assert.Equal((20d, 20d, 0d, 0d), CoordinateGeometry.IntersectEdges((0d, 0d, 10d, 10d), (20d, 20d, 30d, 30d)));
    }

    /// <summary>Integer union retains checked extents, checked edges and degenerate closed bounds.</summary>
    [Fact]
    public void IntegerUnionRetainsCheckedAndClosedPolicies()
    {
        var first = new Rect2D<int>(-10, -20, 12, 24);
        var second = new Rect2D<int>(1, 2, 8, 9);
        Assert.Equal(new(-10, -20, 19, 31), first.Union(second));
        var empty = new Rect2D<int>(int.MinValue, int.MinValue, 0, 0);
        Assert.Throws<OverflowException>(() => empty.Union(second));
        Assert.Throws<OverflowException>(() => second.Union(empty));
        Assert.Throws<OverflowException>(() => empty.Union(default));
        Assert.Equal(new Rect2D<int>(-1, -2, 10, 13), new Rect2D<int>(-1, -2, 0, 0).Union(second));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(int.MaxValue, 0, 1, 1));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(int.MinValue, 0, 1, 1).Union(new(0, 0, 1, 1)));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(0, int.MinValue, 1, 1).Union(new(0, 0, 1, 1)));
        Assert.Equal(new Rect2D<long>(long.MinValue, 0, 2, 1), new Rect2D<long>(long.MinValue, 0, 1, 1).Union(new(long.MinValue + 1, 0, 1, 1)));
    }

    private static void AssertBits((double Left, double Top, double Right, double Bottom) expected, (double Left, double Top, double Right, double Bottom) actual)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Left), BitConverter.DoubleToInt64Bits(actual.Left));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Top), BitConverter.DoubleToInt64Bits(actual.Top));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Right), BitConverter.DoubleToInt64Bits(actual.Right));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Bottom), BitConverter.DoubleToInt64Bits(actual.Bottom));
    }
}
