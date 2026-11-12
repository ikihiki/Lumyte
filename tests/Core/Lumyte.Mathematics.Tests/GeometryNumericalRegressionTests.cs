using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Protects geometry predicates and projections across mixed numerical scales.</summary>
public sealed class GeometryNumericalRegressionTests
{
    /// <summary>Preserves orientation when each axis has a very different magnitude.</summary>
    [Fact]
    public void MixedAxisOrientationDoesNotInventBoundaryContact()
    {
        Point2D<double> a = default;
        var b = new Point2D<double>(1e200, 1e-200);
        var p = new Point2D<double>(5e199, 9e-201);
        Assert.False(new Segment2D<double>(a, b).Intersects(new Segment2D<double>(p, p)));
    }

    /// <summary>Both polygon fill rules share the same noncollinearity result.</summary>
    /// <param name="evenOdd">Whether to use the even-odd fill rule.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedAxisPolygonDoesNotInventBoundaryContact(bool evenOdd)
    {
        Point2D<double>[] vertices = [default, new(1e200, 1e-200)];
        var p = new Point2D<double>(5e199, 9e-201);
        Assert.False(evenOdd ? Geometry2D.ContainsPolygonEvenOddClosed<double>(vertices, p) : Geometry2D.ContainsPolygonClosed<double>(vertices, p));
    }

    /// <summary>A huge perpendicular coordinate cannot erase the projected component.</summary>
    [Fact]
    public void ProjectionRetainsTinyParallelComponent()
    {
        var segment = new Segment2D<double>(default, new Point2D<double>(1, 0));
        Assert.Equal(new Point2D<double>(1e-200, 0), segment.ClosestPoint(new Point2D<double>(1e-200, 1e200)));
    }

    /// <summary>Projection is independent of axis ordering in three dimensions.</summary>
    /// <param name="axis">The nonzero segment axis.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ProjectionRetainsTinyParallelComponentInEveryAxis(int axis)
    {
        Point3D<double> end = axis == 0 ? new(1, 0, 0) : axis == 1 ? new(0, 1, 0) : new(0, 0, 1);
        Point3D<double> query = axis == 0 ? new(1e-200, 1e200, 0) : axis == 1 ? new(0, 1e-200, 1e200) : new(1e200, 0, 1e-200);
        Point3D<double> expected = axis == 0 ? new(1e-200, 0, 0) : axis == 1 ? new(0, 1e-200, 0) : new(0, 0, 1e-200);
        Assert.Equal(expected, new Segment3D<double>(default, end).ClosestPoint(query));
    }

    /// <summary>Representable projected coordinates survive an unrepresentably small parameter.</summary>
    /// <param name="precision">The IEEE precision to exercise.</param>
    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    [InlineData("Half")]
    public void ProjectionRetainsCoordinatesWhenParameterUnderflows(string precision)
    {
        if (precision == "double")
        {
            CheckUnderflowingProjection<double>(1e200, 1e-200);
        }
        else if (precision == "float")
        {
            CheckUnderflowingProjection<float>(1e30f, 1e-30f);
        }
        else
        {
            CheckUnderflowingProjection<Half>((Half)32768, Half.Epsilon);
        }
    }

    /// <summary>Corner centers remain accurate when translation exceeds radius precision.</summary>
    [Fact]
    public void TranslatedRoundedRectangleIncludesSideMidpoints()
    {
        double x = 9007199254740992;
        var shape = new RoundedRect2D<double>(new Rect2D<double>(x, 0, 2, 2), 1);
        Assert.True(shape.Contains(new Point2D<double>(x + 2, 1)));
        Assert.True(shape.Contains(new Point2D<double>(x, 1)));
        Assert.False(shape.Contains(new Point2D<double>(x + 2, 0)));
    }

    /// <summary>Predicates preserve signs even when coordinate differences exceed the selected precision.</summary>
    [Fact]
    public void OppositeExtremeDifferencesHaveTheSamePredicateResult()
    {
        var a = new Point2D<double>(-double.MaxValue, 0);
        var b = new Point2D<double>(double.MaxValue, 1);
        Point2D<double>[] vertices = [a, b];
        Assert.False(new Segment2D<double>(a, b).Intersects(default));
        Assert.False(Geometry2D.ContainsPolygonClosed<double>(vertices, default));
        Assert.False(Geometry2D.ContainsPolygonEvenOddClosed<double>(vertices, default));
    }

    private static void CheckUnderflowingProjection<T>(T large, T small)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        Assert.Equal(T.Zero, small / large);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            T direction = T.CreateChecked(sign);
            T projected = direction * small;
            T end = direction * large;
            var expected2D = new Point2D<T>(projected, T.Zero);
            Assert.Equal(expected2D, new Segment2D<T>(default, new(end, T.Zero)).ClosestPoint(expected2D));
            Assert.Equal(expected2D, new Segment2D<T>(default, new(end, T.Zero)).ClosestPoint(new(projected, large)));
            Assert.Equal(default, new Segment2D<T>(default, new(end, T.Zero)).ClosestPoint(new(-projected, large)));
            Assert.Equal(new Point2D<T>(end, T.Zero), new Segment2D<T>(default, new(end, T.Zero)).ClosestPoint(new(end, large)));
            for (int axis = 0; axis < 3; axis++)
            {
                Point3D<T> endpoint = axis == 0 ? new(end, T.Zero, T.Zero) : axis == 1 ? new(T.Zero, end, T.Zero) : new(T.Zero, T.Zero, end);
                Point3D<T> query = axis == 0 ? new(projected, large, T.Zero) : axis == 1 ? new(T.Zero, projected, large) : new(large, T.Zero, projected);
                Point3D<T> expected = axis == 0 ? new(projected, T.Zero, T.Zero) : axis == 1 ? new(T.Zero, projected, T.Zero) : new(T.Zero, T.Zero, projected);
                Assert.Equal(expected, new Segment3D<T>(default, endpoint).ClosestPoint(query));
            }
        }
    }
}
