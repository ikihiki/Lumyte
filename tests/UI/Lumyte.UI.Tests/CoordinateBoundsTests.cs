using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.UI.Tests;

/// <summary>Checks canonical half-open bounds and retained UI adapters.</summary>
public sealed class CoordinateBoundsTests
{
    /// <summary>Checks origin and size addition retains double precision for float inputs.</summary>
    [Fact]
    public void InputBoundsRetainSmallSizeAtLargeOrigin()
    {
        Vector2 origin = new(16777216, 0);
        Vector2 size = new(1, 1);
        var bounds = UiInputBounds.From(origin, size);
        Assert.Equal(16777217d, bounds.Right);
        Assert.True(bounds.Contains(origin));
        Assert.True(UiInputGeometry.Contains(origin, size, origin));
        Assert.False(bounds.Contains(new(16777218, 0)));
        Assert.False(bounds.Contains(new(origin.X, 1)));
    }

    /// <summary>Checks intersection retains reversed edges while union ignores empty regions.</summary>
    [Fact]
    public void EmptyIntersectionDoesNotPolluteUnion()
    {
        var first = new Bounds2D<double>(0, 0, 10, 10);
        var second = new Bounds2D<double>(20, 20, 30, 30);
        Bounds2D<double> clipped = first.Intersect(second);
        Assert.Equal(new(20, 20, 10, 10), clipped);
        Assert.True(clipped.IsEmpty);
        Assert.Equal(first, clipped.Union(first));
        Assert.Equal(first, first.Union(Bounds2D<double>.Empty));
        Assert.Equal(first, first.Intersect(Bounds2D<double>.Unbounded));
        Assert.False(clipped.Contains(20, 20));
        Assert.Equal(new UiInputBounds(0, 0, 10, 10), new UiInputBounds(20, 20, 10, 10).Union(new(0, 0, 10, 10)));
    }

    /// <summary>Checks union preserves the original comparison and arithmetic policy for NaN edges.</summary>
    [Fact]
    public void InputUnionMatchesLegacyNaNOracle()
    {
        UiInputBounds[] inputs = [new(0, 0, 10, 10), new(double.NaN, 0, 10, 10), new(0, double.NaN, 10, 10), new(0, 0, double.NaN, 10), new(0, 0, 10, double.NaN), new(20, 20, 10, 10), default];
        foreach (UiInputBounds first in inputs)
        {
            foreach (UiInputBounds second in inputs)
            {
                UiInputBounds expected = first.Left >= first.Right || first.Top >= first.Bottom ? second
                    : second.Left >= second.Right || second.Top >= second.Bottom ? first
                    : new(Math.Min(first.Left, second.Left), Math.Min(first.Top, second.Top), Math.Max(first.Right, second.Right), Math.Max(first.Bottom, second.Bottom));
                Assert.Equal(expected, first.Union(second));
            }
        }
    }

    /// <summary>Checks signed-zero and infinite edge operations against the original arithmetic.</summary>
    [Fact]
    public void InputBoundsMatchSignedZeroAndInfinityOracle()
    {
        UiInputBounds[] inputs = [new(-0d, -0d, 1, 1), new(0d, 0d, 2, 2), new(double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity), new(0, 0, -0d, 1)];
        foreach (UiInputBounds first in inputs)
        {
            foreach (UiInputBounds second in inputs)
            {
                UiInputBounds expected = first.Left >= first.Right || first.Top >= first.Bottom ? second
                    : second.Left >= second.Right || second.Top >= second.Bottom ? first
                    : new(Math.Min(first.Left, second.Left), Math.Min(first.Top, second.Top), Math.Max(first.Right, second.Right), Math.Max(first.Bottom, second.Bottom));
                AssertBits(expected, first.Union(second));
                AssertBits(new(Math.Max(first.Left, second.Left), Math.Max(first.Top, second.Top), Math.Min(first.Right, second.Right), Math.Min(first.Bottom, second.Bottom)), first.Intersect(second));
            }
        }

        Assert.True(inputs[2].Contains(new(float.NegativeInfinity, float.NegativeInfinity)));
        Assert.False(inputs[2].Contains(new(float.PositiveInfinity, 0)));
    }

    /// <summary>Checks zero size and IEEE sentinel coordinates require no finite construction.</summary>
    [Fact]
    public void SentinelAndZeroAreaBoundsAreSupported()
    {
        Assert.True(default(Bounds2D<float>).IsEmpty);
        Assert.False(default(Bounds2D<float>).Contains(0, 0));
        Assert.True(Bounds2D<float>.Unbounded.Contains(float.MaxValue, -float.MaxValue));
        Assert.False(Bounds2D<float>.Unbounded.Contains(float.PositiveInfinity, 0));
        var invalid = new Bounds2D<double>(double.NaN, 0, 10, 10);
        Assert.True(invalid.IsEmpty);
        Assert.False(invalid.Contains(1, 1));
        Assert.Equal(new Bounds2D<double>(0, 0, 10, 10), invalid.Union(new(0, 0, 10, 10)));
        Assert.True(float.IsNaN(new Bounds2D<float>(float.NaN, 0, 10, 10).Width));
        Assert.Equal(0, new Bounds2D<float>(20, 0, 10, 10).Width);
    }

    private static void AssertBits(UiInputBounds expected, UiInputBounds actual)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Left), BitConverter.DoubleToInt64Bits(actual.Left));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Top), BitConverter.DoubleToInt64Bits(actual.Top));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Right), BitConverter.DoubleToInt64Bits(actual.Right));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Bottom), BitConverter.DoubleToInt64Bits(actual.Bottom));
    }
}
