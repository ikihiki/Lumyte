using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Verifies generalized shape storage, exact predicates and checked boundaries.</summary>
public sealed class NumericShapeTests
{
    /// <summary>Exercises every built-in numeric scalar with actual predicates and factories.</summary>
    [Fact]
    public void ShapesSupportEveryNumericCategory()
    {
        Check<Half>();
        Check<float>();
        Check<double>();
        Check<decimal>();
        Check<sbyte>();
        Check<byte>();
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
    }

    /// <summary>Unsigned differences and extreme signed differences must not wrap or overflow.</summary>
    [Fact]
    public void ExactPredicatesWidenBeforeSubtractingAndSquaring()
    {
        Assert.True(new Circle2D<uint>(new(10, 10), 5).Contains(new(7, 6)));
        Assert.False(new Circle2D<ulong>(new(ulong.MaxValue, 0), ulong.MaxValue).Contains(new(0, 1)));
        Assert.False(new Sphere3D<Int128>(new(Int128.MinValue, 0, 0), Int128.MaxValue).Contains(new(Int128.MaxValue, 0, 0)));
        Assert.True(new Ellipse2D<UInt128>(new(UInt128.MaxValue, 0), UInt128.MaxValue, 1).Contains(new(0, 0)));
        var huge = BigInteger.Pow(10, 400);
        Assert.True(new Circle2D<BigInteger>(new(huge, huge), 5).Contains(new(huge + 3, huge + 4)));
        Assert.False(new Ellipse2D<BigInteger>(new(huge, huge), huge, huge).Contains(new(2 * huge, 2 * huge)));
        Assert.True(new Sphere3D<BigInteger>(new(huge, huge, huge), huge).Contains(new(2 * huge, huge, huge)));
        Assert.True(new RoundedRect2D<BigInteger>(new(huge, huge, 10, 10), 5).Contains(new(huge, huge + 5)));
    }

    /// <summary>Decimal predicates use exact rational products, not rounded square roots or doubles.</summary>
    [Fact]
    public void DecimalPredicatesPreserveLastDigitAndAvoidIntermediateOverflow()
    {
        Assert.False(new Circle2D<decimal>(default, 1).Contains(new(1, 0.0000000000000000000000000001m)));
        Assert.False(new Ellipse2D<decimal>(default, 1, 1).Contains(new(1, 0.0000000000000000000000000001m)));
        Assert.True(new Circle2D<decimal>(default, decimal.MaxValue).Contains(new(decimal.MaxValue, 0)));
        Assert.False(new Sphere3D<decimal>(default, decimal.MaxValue).Contains(new(decimal.MaxValue, 1, 0)));
        Assert.True(new Ellipse2D<decimal>(default, decimal.MaxValue, decimal.MaxValue).Contains(new(decimal.MaxValue, 0)));
        Assert.False(new RoundedRect2D<decimal>(new(0, 0, 2, 2), 1).Contains(new(0, 0.9999999999999999999999999999m)));
    }

    /// <summary>Half-integer clamps are rejected explicitly when the scalar cannot store them.</summary>
    [Fact]
    public void DiscreteHalfExtentPolicyIsExplicit()
    {
        Assert.Equal(0, new RoundedRect2D<int>(new(0, 0, 3, 3), 0).Radius);
        Assert.Equal(1, new RoundedRect2D<int>(new(0, 0, 3, 3), 1).Radius);
        Assert.Equal(1u, new RoundedRect2D<uint>(new(0, 0, 3, 3), 1).Radius);
        Assert.Equal(0, new RoundedRect2D<int>(new(0, 0, 0, 3), int.MaxValue).Radius);
        Assert.Equal(1, new RoundedRect2D<int>(new(0, 0, 2, 2), int.MaxValue).Radius);
        Assert.Throws<ArgumentException>(() => new RoundedRect2D<int>(new(0, 0, 3, 3), 2));
        Assert.Throws<ArgumentException>(() => Rect2D<int>.FromCenterSize(new(5, 5), 3, 4));
        Assert.False(RoundedRect2D<uint>.TryCreate(new(0, 0, 3, 3), 2, out _));
        Assert.Equal(1.5m, new RoundedRect2D<decimal>(new(0, 0, 3, 3), 2).Radius);
        Assert.True(new RoundedRect2D<uint>(new(0, 0, 4, 4), 2).Contains(new(0, 2)));
        Assert.False(new RoundedRect2D<uint>(new(0, 0, 4, 4), 2).Contains(new(0, 0)));
    }

    /// <summary>Checked storage and transformations reject overflow and retain closed degeneracies.</summary>
    [Fact]
    public void RectangleArithmeticIsCheckedAndDegeneraciesSurvive()
    {
        Assert.False(Rect2D<byte>.TryCreate(255, 0, 1, 1, out _));
        Assert.Throws<OverflowException>(() => new Rect2D<uint>(0, 0, 1, 1).OffsetInverse(1, 0));
        Assert.Throws<OverflowException>(() => new Rect2D<uint>(0, 0, 1, 1).Outset(1, 0));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(int.MinValue, 0, 1, 1).Union(new(0, 0, 1, 1)));
        Assert.Throws<OverflowException>(() => new Rect2D<int>(256, 0, 1, 1).ConvertChecked<byte>());
        var first = new Rect2D<int>(int.MinValue, 0, 0, 1);
        var second = new Rect2D<int>(int.MaxValue, 0, 0, 1);
        Assert.Throws<OverflowException>(() => first.Union(second));
        Assert.Throws<OverflowException>(() => first.Outset(1, 1));
        Assert.False(new Rect2D<uint>(0, 0, 1, 1).TryIntersect(new(10, 10, 1, 1), out Rect2D<uint> missing));
        Assert.Equal(default, missing);
        Assert.True(Bounds2D<int>.Empty.IsEmpty);
        Assert.False(Bounds2D<BigInteger>.TryGetUnbounded(out _));
        Assert.Throws<NotSupportedException>(() => Bounds2D<decimal>.Unbounded);
        Assert.True(Bounds2D<double>.TryGetUnbounded(out Bounds2D<double> unbounded));
        Assert.True(unbounded.Contains(double.MinValue, double.MaxValue));
        Assert.Equal(0u, new Bounds2D<uint>(10, 10, 0, 0).Width);
    }

    /// <summary>Closed rectangles preserve points and lines in unions, outsets and intersections.</summary>
    [Fact]
    public void ClosedRectangleOperationsIncludeDegenerateBoundaries()
    {
        var point = new Rect2D<int>(10, 20, 0, 0);
        var line = new Rect2D<int>(12, 18, 0, 5);
        var area = new Rect2D<int>(0, 0, 2, 3);
        Assert.True(point.Contains(new(10, 20)));
        Assert.Equal(new Rect2D<int>(0, 0, 10, 20), area.Union(point));
        Assert.Equal(area.Union(point), point.Union(area));
        Assert.Equal(new Rect2D<int>(10, 18, 2, 5), point.Union(line));
        Assert.Equal(new Rect2D<int>(9, 18, 2, 4), point.Outset(1, 2));
        Assert.Equal(new Rect2D<int>(11, 17, 2, 7), line.Outset(1, 1));
        Assert.True(area.TryIntersect(new(2, 1, 4, 4), out Rect2D<int> edge));
        Assert.Equal(new Rect2D<int>(2, 1, 0, 2), edge);
        Assert.True(area.TryIntersect(new(2, 3, 1, 1), out Rect2D<int> corner));
        Assert.Equal(new Rect2D<int>(2, 3, 0, 0), corner);
        Assert.True(point.TryIntersect(point, out Rect2D<int> same));
        Assert.Equal(point, same);
        Assert.False(area.TryIntersect(point, out _));
        Assert.False(point.TryIntersect(area, out _));
        Assert.False(area.TryIntersect(new(0, 4, 1, 1), out _));
    }

    /// <summary>Point bounds reduce unordered coordinates without narrowing decimal or huge integer values.</summary>
    [Fact]
    public void PointBoundsPreservePreciseCoordinates()
    {
        const decimal Lower = 1.0000000000000000000000000001m;
        const decimal Upper = 1.0000000000000000000000000003m;
        Point2D<decimal>[] points = [new(Upper, Lower), new(Lower, Upper), new(Lower, Lower)];
        var rect = Rect2D<decimal>.FromPoints(points);
        Assert.Equal(new Point2D<decimal>(Lower, Lower), rect.Min);
        Assert.Equal(new Point2D<decimal>(Upper, Upper), rect.Max);
        Assert.Equal(0.0000000000000000000000000002m, rect.Width);
        Assert.True(Rect2D<decimal>.TryFromPoints(points, out Rect2D<decimal> triedRect));
        Assert.Equal(rect, triedRect);
        Point3D<decimal>[] points3 = [new(Upper, Lower, Upper), new(Lower, Upper, Lower)];
        var box = AxisAlignedBox3D<decimal>.FromPoints(points3);
        Assert.Equal(new Point3D<decimal>(Lower, Lower, Lower), box.Min);
        Assert.Equal(new Point3D<decimal>(Upper, Upper, Upper), box.Max);
        Assert.True(AxisAlignedBox3D<decimal>.TryFromPoints(points3, out AxisAlignedBox3D<decimal> triedBox));
        Assert.Equal(box, triedBox);

        var huge = BigInteger.Pow(10, 400);
        Point2D<BigInteger>[] hugePoints = [new(huge + 3, -huge), new(huge, -huge + 7)];
        Assert.Equal(new Rect2D<BigInteger>(huge, -huge, 3, 7), Rect2D<BigInteger>.FromPoints(hugePoints));
        Point3D<BigInteger>[] hugePoints3 = [new(huge + 3, -huge, huge + 2), new(huge, -huge + 7, -huge)];
        Assert.Equal(new AxisAlignedBox3D<BigInteger>(new(huge, -huge, -huge), new(huge + 3, -huge + 7, huge + 2)), AxisAlignedBox3D<BigInteger>.FromPoints(hugePoints3));
    }

    /// <summary>Unsigned endpoints, repeated points and singletons produce closed bounds independent of input order.</summary>
    [Fact]
    public void PointBoundsIncludeUnsignedEndpointsAndDegeneracies()
    {
        Point2D<uint>[] points = [new(uint.MaxValue, 0), new(0, uint.MaxValue), new(uint.MaxValue, 0)];
        var rect = Rect2D<uint>.FromPoints(points);
        Assert.Equal(new Rect2D<uint>(0, 0, uint.MaxValue, uint.MaxValue), rect);
        Array.Reverse(points);
        Assert.Equal(rect, Rect2D<uint>.FromPoints(points));
        Assert.True(rect.Contains(points[0]));
        Assert.Equal(new Rect2D<uint>(uint.MaxValue, 0, 0, 0), Rect2D<uint>.FromPoints(points.AsSpan(0, 1)));
        Point3D<uint>[] points3 = [new(uint.MaxValue, 0, uint.MaxValue), new(0, uint.MaxValue, 0)];
        var box = AxisAlignedBox3D<uint>.FromPoints(points3);
        Assert.Equal(new AxisAlignedBox3D<uint>(default, new(uint.MaxValue, uint.MaxValue, uint.MaxValue)), box);
        Array.Reverse(points3);
        Assert.Equal(box, AxisAlignedBox3D<uint>.FromPoints(points3));
        Assert.Equal(new AxisAlignedBox3D<uint>(points3[0], points3[0]), AxisAlignedBox3D<uint>.FromPoints(points3.AsSpan(0, 1)));
    }

    /// <summary>Empty input fails explicitly, while checked rectangle extents may overflow valid endpoints.</summary>
    [Fact]
    public void PointBoundsRejectEmptyInputAndUnrepresentableExtents()
    {
        Assert.Equal("points", Assert.Throws<ArgumentException>(() => Rect2D<int>.FromPoints([])).ParamName);
        Assert.Equal("points", Assert.Throws<ArgumentException>(() => AxisAlignedBox3D<int>.FromPoints([])).ParamName);
        Assert.False(Rect2D<int>.TryFromPoints([], out Rect2D<int> emptyRect));
        Assert.Equal(default, emptyRect);
        Assert.False(AxisAlignedBox3D<int>.TryFromPoints([], out AxisAlignedBox3D<int> emptyBox));
        Assert.Equal(default, emptyBox);
        Point2D<int>[] points = [new(int.MaxValue, 0), new(int.MinValue, 0)];
        Assert.Throws<OverflowException>(() => Rect2D<int>.FromPoints(points));
        Assert.False(Rect2D<int>.TryFromPoints(points, out Rect2D<int> overflowRect));
        Assert.Equal(default, overflowRect);
        Array.Reverse(points);
        Assert.Throws<OverflowException>(() => Rect2D<int>.FromPoints(points));
        Assert.False(Rect2D<int>.TryFromPoints(points, out overflowRect));
        Assert.Equal(default, overflowRect);
        Point2D<double>[] floating = [new(-double.MaxValue, 0), new(double.MaxValue, 0)];
        Assert.Throws<OverflowException>(() => Rect2D<double>.FromPoints(floating));
        Assert.False(Rect2D<double>.TryFromPoints(floating, out Rect2D<double> floatingRect));
        Assert.Equal(default, floatingRect);
        Point3D<int>[] points3 = [new(int.MaxValue, 0, int.MinValue), new(int.MinValue, 0, int.MaxValue)];
        Assert.True(AxisAlignedBox3D<int>.TryFromPoints(points3, out AxisAlignedBox3D<int> box));
        Assert.Equal(new Point3D<int>(int.MinValue, 0, int.MinValue), box.Min);
        Assert.Equal(new Point3D<int>(int.MaxValue, 0, int.MaxValue), box.Max);
    }

    private static void Check<T>()
        where T : INumber<T>
    {
        T zero = T.Zero;
        T one = T.One;
        T two = T.CreateChecked(2);
        T three = T.CreateChecked(3);
        T four = T.CreateChecked(4);
        T five = T.CreateChecked(5);
        var point = new Point2D<T>(three, four);
        var circle = new Circle2D<T>(default, five);
        Assert.True(circle.Contains(point));
        Assert.False(new Circle2D<T>(default, four).Contains(point));
        Assert.True(new Sphere3D<T>(default, five).Contains(new(zero, three, four)));
        Assert.True(new Ellipse2D<T>(default, five, five).Contains(point));
        Assert.False(new Ellipse2D<T>(default, three, four).Contains(point));
        Assert.True(new Ellipse2D<T>(default, zero, four).Contains(new(zero, four)));
        var rect = new Rect2D<T>(zero, zero, four, four);
        Assert.True(rect.Contains(new(four, four)));
        Assert.False(rect.ContainsHalfOpen(new(four, four)));
        Assert.True(rect.ContainsHalfOpen(default));
        Assert.Equal(rect, Rect2D<T>.FromMinMax(default, new(four, four)));
        Assert.Equal(rect, Rect2D<T>.FromPoints([new(four, zero), new(zero, four)]));
        Assert.True(Rect2D<T>.TryFromPoints([new(four, zero), new(zero, four)], out Rect2D<T> pointBounds));
        Assert.Equal(rect, pointBounds);
        Assert.Equal(rect, Rect2D<T>.FromCenterSize(new(two, two), four, four));
        Assert.True(Rect2D<T>.TryCreate(zero, zero, four, four, out Rect2D<T> created));
        Assert.Equal(rect, created);
        Assert.Equal(rect, rect.Offset(one, one).OffsetInverse(one, one));
        Assert.Equal(rect, rect.ConvertChecked<T>());
        Assert.True(Circle2D<T>.TryCreate(default, five, out _));
        Assert.True(Sphere3D<T>.TryCreate(default, five, out _));
        Assert.True(Ellipse2D<T>.TryCreate(default, five, four, out _));
        Assert.True(RoundedRect2D<T>.TryCreate(rect, two, out RoundedRect2D<T> rounded));
        Assert.True(rounded.Contains(new(zero, two)));
        Assert.False(rounded.Contains(default));
        Assert.Equal(two, rounded.Radius);
        Assert.Equal(circle, circle.ConvertChecked<T>());
        Assert.Equal(rounded, rounded.ConvertChecked<T>());
        Assert.Equal(new Ellipse2D<T>(default, five, four), new Ellipse2D<T>(default, five, four).ConvertChecked<T>());
        Assert.Equal(new Sphere3D<T>(default, five), new Sphere3D<T>(default, five).ConvertChecked<T>());
        var box = AxisAlignedBox3D<T>.FromCenterSize(new(two, two, two), four, four, four);
        Assert.True(box.Contains(new(four, four, four)));
        Assert.Equal(box, AxisAlignedBox3D<T>.FromPoints([new(four, zero, four), new(zero, four, zero)]));
        Assert.True(AxisAlignedBox3D<T>.TryFromPoints([new(four, zero, four), new(zero, four, zero)], out AxisAlignedBox3D<T> pointBox));
        Assert.Equal(box, pointBox);
        Assert.True(box.TryIntersect(default, out _));
        Assert.Equal(box, box.ConvertChecked<T>());
        Assert.Equal(box, box.Union(default));
        Assert.False(box.TryIntersect(new(new(five, five, five), new(five, five, five)), out _));
        Assert.True(Bounds2D<T>.Empty.IsEmpty);
        Assert.Equal(new Bounds2D<T>(zero, zero, four, four), Bounds2D<T>.FromCenterSize(new(two, two), four, four));
        Assert.True(Bounds2D<T>.TryCreate(zero, zero, four, four, out Bounds2D<T> raw));
        Assert.Equal(raw, raw.ConvertChecked<T>());
        Assert.True(new Bounds2D<T>(zero, zero, four, four).Contains(one, one));
        Assert.True(default(Rect2D<T>).IsEmpty);
        Assert.True(default(Rect2D<T>).Contains(default));
        Assert.False(default(Rect2D<T>).ContainsHalfOpen(default));
        Assert.True(default(Circle2D<T>).Contains(default));
        Assert.True(default(Sphere3D<T>).Contains(default));
        Assert.True(default(Ellipse2D<T>).Contains(default));
        Assert.True(default(RoundedRect2D<T>).Contains(default));
    }
}
