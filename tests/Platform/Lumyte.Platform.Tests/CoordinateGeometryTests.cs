using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Platform.Tests;

/// <summary>Checks shared coordinate operations preserve the permissive domain boundary contracts.</summary>
public sealed class CoordinateGeometryTests
{
    /// <summary>Checks degenerate and disjoint intersections retain their lower-edge anchors.</summary>
    [Fact]
    public void IntersectionsPreserveEmptyAnchors()
    {
        Assert.Equal((20f, 30f, 0f, 0f), Geometry2D.IntersectBoundsClamped((0f, 0f, 10f, 10f), (20f, 30f, 1f, 1f)));
        Assert.Equal((5f, 2f, 0f, 4f), Geometry2D.IntersectBoundsClamped((5f, 2f, 0f, 4f), (0f, 0f, 10f, 10f)));
        Assert.False(Geometry2D.ContainsHalfOpenBounds(5f, 2f, 0f, 4f, 5f, 3f));
        Assert.False(Geometry2D.ContainsHalfOpenBounds(0f, 0f, 10f, 10f, float.NaN, 3f));
    }

    /// <summary>Checks edge-form clipping does not lose an upper edge through cancellation.</summary>
    [Fact]
    public void EdgeIntersectionPreservesOriginalUpperEdges()
    {
        Assert.Equal((-1e20f, 0f, 1e20f, 1f), Geometry2D.IntersectBoundsClampedEdges((-1e20f, 0f, 1f, 1f), (-1e20f, 0f, 2f, 1f)));
        Assert.Equal((1f, 2f, 3f, 4f), Geometry2D.UnionBoundsIgnoringZeroArea((99f, 99f, 0f, 1f), (1f, 2f, 3f, 4f)));
        Assert.Equal((99f, 99f, 0f, 1f), Geometry2D.UnionBoundsIgnoringZeroArea((1f, 2f, 3f, 0f), (99f, 99f, 0f, 1f)));
    }

    /// <summary>Checks generic integer storage, default values and arithmetic without floating-point narrowing.</summary>
    [Fact]
    public void IntegerRectanglesSupportMultipleScalarTypes()
    {
        var bounds = new Rect2D<long>(9007199254740993, -3, 2, 4);
        Assert.Equal(new Rect2D<long>(9007199254740994, -5, 2, 4), bounds.Offset(1, -2));
        Assert.Equal(bounds, bounds.Offset(1, -2).OffsetInverse(1, -2));
        Assert.Equal(new Rect2D<long>(9007199254740991, -6, 6, 10), bounds.Outset(2, 3));
        Assert.Equal(new Rect2D<long>(9007199254740993, -3, 5, 6), bounds.Union(new(9007199254740996, -1, 2, 4)));
        Assert.True(default(Rect2D<int>).IsEmpty);
        Assert.Equal(new Rect2D<uint>(2, 3, 4, 5), new Rect2D<uint>(3, 4, 2, 3).Outset(1, 1));
        Assert.Throws<OverflowException>(() => new Rect2D<uint>(0, 0, 1, 1).OffsetInverse(1, 0));
        System.Numerics.BigInteger huge = System.Numerics.BigInteger.One << 256;
        Assert.Equal(new Rect2D<System.Numerics.BigInteger>(huge - 2, -3, 5, 7), new Rect2D<System.Numerics.BigInteger>(huge, 0, 1, 1).Outset(2, 3));
    }

    /// <summary>Checks checked arithmetic, constructor argument names and closed degenerate bounds.</summary>
    [Fact]
    public void IntegerRectanglesValidateEveryArithmeticBoundary()
    {
        Assert.Equal("width", Assert.Throws<ArgumentOutOfRangeException>(() => new Rect2D<int>(0, 0, -1, 1)).ParamName);
        Assert.Equal("height", Assert.Throws<ArgumentOutOfRangeException>(() => new Rect2D<int>(0, 0, 1, -1)).ParamName);
        Assert.Throws<OverflowException>(() => new Rect2D<int>(int.MaxValue, 0, 1, 1));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(0, int.MaxValue, 1, 1));
        Assert.Equal(new Rect2D<int>(0, 0, 1, 1), new Rect2D<int>(int.MinValue, int.MinValue, 1, 1).OffsetInverse(int.MinValue, int.MinValue));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(0, 0, 1, 1).OffsetInverse(int.MinValue, 0));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(int.MaxValue - 1, 0, 1, 1).Offset(1, 0));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(0, 0, 1, 1).Outset(1073741824, 0));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(0, 0, int.MaxValue, 1).Outset(1, 0));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(int.MinValue, 0, 1, 1).Union(new(0, 0, 1, 1)));
        var first = new Rect2D<int>(int.MinValue, 0, 0, 1);
        var second = new Rect2D<int>(int.MaxValue, 0, 0, 1);
        Assert.Throws<OverflowException>(() => first.Union(second));
        Assert.Throws<OverflowException>(() => second.Union(first));
        Assert.Throws<OverflowException>(() => first.Outset(int.MaxValue, int.MaxValue));
        Assert.Equal("x", Assert.Throws<ArgumentOutOfRangeException>(() => first.Outset(-1, 0)).ParamName);
        Assert.Equal("y", Assert.Throws<ArgumentOutOfRangeException>(() => first.Outset(0, -1)).ParamName);
        Assert.Throws<OverflowException>(() => first.Offset(-1, 0));
    }
}
