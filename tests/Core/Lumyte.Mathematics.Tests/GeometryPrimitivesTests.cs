using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Verifies immutable generic geometry, numerical limits, and degenerate closed sets.</summary>
public sealed class GeometryPrimitivesTests
{
    /// <summary>Exercises the shared shape contract with each supported component precision.</summary>
    [Fact]
    public void ShapesSupportHalfFloatAndDouble()
    {
        CheckShapes<Half>();
        CheckShapes<float>();
        CheckShapes<double>();
    }

    /// <summary>Checks precision preservation, scaling, and explicit overflow failures.</summary>
    [Fact]
    public void DistancesPreservePrecisionAndAvoidSquaredOverflow()
    {
        Assert.Equal(1, new Point2D<double>(16777217, 0).DistanceTo(new Point2D<double>(16777216, 0)));
        Assert.Equal(1e200, new Point2D<double>(1e200, 0).DistanceTo(default));
        Assert.Equal(1e-200, new Point3D<double>(0, 0, 1e-200).DistanceTo(default));
        Assert.Equal(float.Epsilon, new Point2D<float>(float.Epsilon, 0).DistanceTo(default));
        Assert.Equal(1e30f, new Point3D<float>(1e30f, 0, 0).DistanceTo(default));
        Assert.Equal((Half)60000, new Point2D<Half>((Half)60000, (Half)0).DistanceTo(default));
        Assert.Throws<OverflowException>(() => new Point2D<double>(double.MaxValue, 0).DistanceTo(new Point2D<double>(-double.MaxValue, 0)));
        Assert.Throws<OverflowException>(() => new Point3D<double>(double.MaxValue, double.MaxValue, 0).DistanceTo(default));
        Assert.Throws<OverflowException>(() => new Circle2D<double>(default, 1).Contains(new Point2D<double>(double.MaxValue, double.MaxValue)));
    }

    /// <summary>Checks scaled segment projection for huge, tiny, endpoint, and zero-length cases.</summary>
    [Fact]
    public void ProjectionAvoidsSquaredOverflowAndUnderflow()
    {
        var huge = new Segment2D<double>(default, new Point2D<double>(1e200, 0));
        Assert.Equal(new Point2D<double>(5e199, 0), huge.ClosestPoint(new Point2D<double>(5e199, 3)));
        Assert.Equal(3, huge.DistanceTo(new Point2D<double>(5e199, 3)));
        var tiny = new Segment3D<double>(default, new Point3D<double>(0, 0, 1e-200));
        Assert.Equal(new Point3D<double>(0, 0, 5e-201), tiny.ClosestPoint(new Point3D<double>(0, 1e-201, 5e-201)));
        Assert.Equal(tiny.End, tiny.ClosestPoint(new Point3D<double>(0, 0, 1e200)));
        Assert.Equal(5, default(Segment2D<double>).DistanceTo(new Point2D<double>(3, 4)));
        Assert.Equal(5, default(Segment3D<double>).DistanceTo(new Point3D<double>(0, 3, 4)));
    }

    /// <summary>Checks proper, collinear, degenerate, and touching segment intersections.</summary>
    [Fact]
    public void SegmentIntersectionIncludesClosedBoundaries()
    {
        var horizontal = new Segment2D<double>(default, new Point2D<double>(4, 0));
        Assert.True(horizontal.Intersects(new Segment2D<double>(new Point2D<double>(2, -1), new Point2D<double>(2, 1))));
        Assert.True(horizontal.Intersects(new Segment2D<double>(new Point2D<double>(2, 0), new Point2D<double>(6, 0))));
        Assert.True(horizontal.Intersects(new Segment2D<double>(new Point2D<double>(4, 0), new Point2D<double>(5, 1))));
        Assert.True(horizontal.Intersects(default));
        Assert.False(horizontal.Intersects(new Segment2D<double>(new Point2D<double>(5, 0), new Point2D<double>(6, 0))));
        Assert.False(horizontal.Intersects(new Segment2D<double>(new Point2D<double>(0, 1), new Point2D<double>(4, 1))));
    }

    /// <summary>Checks validation and finite maximum rectangle coordinates.</summary>
    [Fact]
    public void ConstructorsRejectInvalidComponents()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Point2D<double>(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Point3D<float>(0, 0, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Point2D<Half>(Half.NegativeInfinity, (Half)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Circle2D<double>(default, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Sphere3D<double>(default, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rect2D<double>(0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoundedRect2D<double>(default, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Ellipse2D<double>(default, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AxisAlignedBox3D<double>(new Point3D<double>(1, 0, 0), default));
        Assert.Throws<OverflowException>(() => new Rect2D<double>(double.MaxValue, 0, double.MaxValue, 1));
    }

    /// <summary>Checks clamped rounded corners and degenerate ellipses.</summary>
    [Fact]
    public void RoundedRectanglesAndEllipsesIncludeTheirBoundaries()
    {
        var rounded = new RoundedRect2D<double>(new Rect2D<double>(0, 0, 10, 4), 100);
        Assert.Equal(2, rounded.Radius);
        Assert.True(rounded.Contains(new Point2D<double>(0, 2)));
        Assert.True(rounded.Contains(new Point2D<double>(5, 0)));
        Assert.False(rounded.Contains(default));
        Assert.True(new RoundedRect2D<double>(new Rect2D<double>(0, 0, 0, 4), 2).Contains(new Point2D<double>(0, 2)));
        var ellipse = new Ellipse2D<double>(default, 4, 2);
        Assert.True(ellipse.Contains(new Point2D<double>(4, 0)));
        Assert.True(ellipse.Contains(new Point2D<double>(0, 2)));
        Assert.False(ellipse.Contains(new Point2D<double>(4, 2)));
        Assert.True(new Ellipse2D<double>(default, 0, 2).Contains(new Point2D<double>(0, 1)));
        Assert.False(new Ellipse2D<double>(default, 0, 2).Contains(new Point2D<double>(1, 1)));
    }

    private static void CheckShapes<T>()
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        T zero = T.Zero;
        T one = T.One;
        T three = T.CreateChecked(3);
        T four = T.CreateChecked(4);
        T five = T.CreateChecked(5);
        var point = new Point2D<T>(three, four);
        Assert.Equal(five, point.DistanceTo(default));
        Assert.Equal(five, new Point3D<T>(zero, three, four).DistanceTo(default));
        Assert.True(new Circle2D<T>(default, five).Contains(point));
        Assert.False(new Circle2D<T>(default, four).Contains(point));
        Assert.True(new Sphere3D<T>(default, five).Contains(new Point3D<T>(zero, three, four)));
        var rect = new Rect2D<T>(zero, zero, three, four);
        Assert.True(rect.Contains(point));
        Assert.False(rect.ContainsHalfOpen(point));
        Assert.True(rect.ContainsHalfOpen(default));
        Assert.Equal(point, rect.Max);
        Assert.True(new Rect2D<T>(zero, zero, zero, four).Contains(new Point2D<T>(zero, one)));
        Assert.True(new AxisAlignedBox3D<T>(default, new Point3D<T>(one, three, four)).Contains(new Point3D<T>(one, three, four)));
        Assert.True(default(Circle2D<T>).Contains(default));
        Assert.True(default(Sphere3D<T>).Contains(default));
        Assert.True(default(Rect2D<T>).Contains(default));
        Assert.False(default(Rect2D<T>).ContainsHalfOpen(default));
        Assert.True(default(AxisAlignedBox3D<T>).Contains(default));
        Assert.True(default(RoundedRect2D<T>).Contains(default));
        Assert.True(default(Ellipse2D<T>).Contains(default));
        Assert.Equal(default, new Point2D<T>(-zero, zero));
        Assert.Equal(default(Point2D<T>).GetHashCode(), new Point2D<T>(-zero, zero).GetHashCode());
        T tolerance = T.CreateChecked(0.01);
        Assert.True(new Point2D<T>(three, zero).DistanceTo(new Segment2D<T>(default, new Point2D<T>(five, zero)).ClosestPoint(point)) <= tolerance);
        Assert.True(new Point3D<T>(zero, three, zero).DistanceTo(new Segment3D<T>(default, new Point3D<T>(zero, five, zero)).ClosestPoint(new Point3D<T>(one, three, one))) <= tolerance);
    }
}
