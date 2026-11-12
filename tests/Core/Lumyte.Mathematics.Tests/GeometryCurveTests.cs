using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Verifies generic curve precision, bounded tessellation, and polygon fill rules.</summary>
public sealed class GeometryCurveTests
{
    /// <summary>Checks exact endpoints and analytic midpoint values without a float parameter conversion.</summary>
    [Fact]
    public void EvaluationPreservesDoublePrecisionAndEndpoints()
    {
        var start = new Point2D<double>(0, 0);
        var control = new Point2D<double>(0, 1);
        var end = new Point2D<double>(1, 0);
        Assert.Equal(start, Bezier2D.Quadratic(start, control, end, 0));
        Assert.Equal(end, Bezier2D.Cubic(start, control, control, end, 1));
        Assert.Equal(new Point2D<double>(0.25, 0.5), Bezier2D.Quadratic(start, control, end, 0.5));
        Assert.Equal(new Point2D<double>(0.5, 0.75), Bezier2D.Cubic(start, control, new Point2D<double>(1, 1), end, 0.5));
        double amount = 0.500000000001;
        Assert.Equal(amount, Bezier2D.Quadratic(start, new Point2D<double>(0.5, 0), end, amount).X, 14);
        Assert.Throws<ArgumentOutOfRangeException>(() => Bezier2D.Quadratic(start, control, end, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bezier2D.Cubic(start, control, control, end, 1.1));
    }

    /// <summary>Checks Half arithmetic and interpolation across opposite coordinate limits.</summary>
    [Fact]
    public void EvaluationSupportsHalfAndExtremeCoordinates()
    {
        var start = new Point2D<Half>((Half)0, (Half)0);
        var control = new Point2D<Half>((Half)0, (Half)1);
        var end = new Point2D<Half>((Half)1, (Half)0);
        Assert.Equal(new Point2D<Half>((Half)0.25, (Half)0.5), Bezier2D.Quadratic(start, control, end, (Half)0.5));
        var low = new Point2D<double>(-double.MaxValue, 0);
        var high = new Point2D<double>(double.MaxValue, 0);
        Assert.Equal(new Point2D<double>(0, 0), Bezier2D.Quadratic(low, new Point2D<double>(0, 0), high, 0.5));
    }

    /// <summary>Checks endpoint order and measured distance from sampled cubic points to the polyline.</summary>
    [Fact]
    public void FlattenedCubicRespectsTolerance()
    {
        var start = new Point2D<double>(0, 0);
        var control1 = new Point2D<double>(0, 2);
        var control2 = new Point2D<double>(2, -1);
        var end = new Point2D<double>(2, 0);
        var points = new List<Point2D<double>>();
        Assert.True(Bezier2D.TryFlattenCubic(start, control1, control2, end, 0.01, points, 16, 1024));
        Assert.Equal(start, points[0]);
        Assert.Equal(end, points[^1]);
        Assert.InRange(points.Count, 3, 1024);
        for (int i = 0; i <= 200; i++)
        {
            Point2D<double> sample = Bezier2D.Cubic(start, control1, control2, end, i / 200.0);
            double distance = double.PositiveInfinity;
            for (int j = 1; j < points.Count; j++)
            {
                distance = Math.Min(distance, new Segment2D<double>(points[j - 1], points[j]).DistanceTo(sample));
            }

            Assert.InRange(distance, 0, 0.010000000001);
        }
    }

    /// <summary>Checks quadratic subdivision, coincident endpoints, and transactional budget failure.</summary>
    [Fact]
    public void FlatteningBudgetsAreExplicitAndTransactional()
    {
        var start = new Point2D<double>(0, 0);
        var control = new Point2D<double>(1, 2);
        var end = new Point2D<double>(2, 0);
        var points = new List<Point2D<double>> { end };
        Assert.False(Bezier2D.TryFlattenQuadratic(start, control, end, 0.01, points, 0, 100));
        Assert.False(Bezier2D.TryFlattenQuadratic(start, control, end, 0.01, points, 16, 2));
        Assert.False(Bezier2D.TryFlattenCubic(start, control, control, end, 0.01, points, 0, 100));
        Assert.False(Bezier2D.TryFlattenCubic(start, control, control, end, 0.01, points, 16, 2));
        Assert.Single(points);
        Assert.True(Bezier2D.TryFlattenQuadratic(start, control, start, 0.01, points, 16, 1024));
        Assert.Equal(end, points[0]);
        Assert.Equal(start, points[1]);
        Assert.Equal(start, points[^1]);
        Assert.Contains(control, points.Select(point => new Point2D<double>(point.X * 2, point.Y * 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bezier2D.TryFlattenQuadratic(start, control, end, 0, points, 16, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bezier2D.TryFlattenCubic(start, control, control, end, double.PositiveInfinity, points, 16, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bezier2D.TryFlattenQuadratic(start, control, end, 0.1, points, -1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bezier2D.TryFlattenQuadratic(start, control, end, 0.1, points, 31, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bezier2D.TryFlattenQuadratic(start, control, end, 0.1, points, 16, 1));
    }

    /// <summary>Checks a straight quadratic requires only its endpoints, including with Half coordinates.</summary>
    [Fact]
    public void StraightHalfCurveFitsMinimumBudgets()
    {
        var start = new Point2D<Half>((Half)0, (Half)0);
        var middle = new Point2D<Half>((Half)1, (Half)0);
        var end = new Point2D<Half>((Half)2, (Half)0);
        var points = new List<Point2D<Half>>();
        Assert.True(Bezier2D.TryFlattenQuadratic(start, middle, end, (Half)0.01, points, 0, 2));
        Assert.Equal(new[] { start, end }, points);
    }

    /// <summary>Checks boundaries, horizontal edges, vertex transitions, and reversed contours.</summary>
    [Fact]
    public void PolygonContainmentIncludesBoundaryAndIgnoresOrientation()
    {
        Point2D<double>[] square = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)];
        Point2D<double>[] queries = [new(1, 1), new(0, 0), new(2, 1), new(1, 2), new(0, 1)];
        foreach (Point2D<double> query in queries)
        {
            Assert.True(Polygon2D.Contains<double>(square, query));
            Assert.True(Polygon2D.ContainsEvenOdd<double>(square, query));
        }

        Array.Reverse(square);
        Assert.True(Polygon2D.Contains<double>(square, new Point2D<double>(1, 1)));
        Assert.False(Polygon2D.Contains<double>(square, new Point2D<double>(3, 0)));
        Assert.False(Polygon2D.ContainsEvenOdd<double>(square, new Point2D<double>(3, 2)));
        Point2D<double>[] diamond = [new(0, 1), new(1, 0), new(2, 1), new(1, 2)];
        Assert.True(Polygon2D.ContainsEvenOdd<double>(diamond, new Point2D<double>(1, 1)));
        Assert.False(Polygon2D.ContainsEvenOdd<double>(diamond, new Point2D<double>(-1, 1)));
    }

    /// <summary>Checks nonzero and parity rules differ for a twice-traced contour.</summary>
    [Fact]
    public void PolygonFillRulesDistinguishRepeatedWinding()
    {
        Point2D<double>[] twice = [new(0, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0)];
        Assert.True(Polygon2D.Contains<double>(twice, new Point2D<double>(1, 1)));
        Assert.False(Polygon2D.ContainsEvenOdd<double>(twice, new Point2D<double>(1, 1)));
        Assert.True(Polygon2D.ContainsEvenOdd<double>(twice, new Point2D<double>(0, 1)));
        Assert.False(Polygon2D.Contains<double>([], new Point2D<double>(0, 0)));
        Assert.True(Polygon2D.Contains<double>([new(1, 1)], new Point2D<double>(1, 1)));
        Assert.False(Polygon2D.Contains<double>([new(1, 1)], new Point2D<double>(1, 2)));
        Assert.True(Polygon2D.Contains<double>([new(0, 0), new(2, 2)], new Point2D<double>(1, 1)));
        Assert.False(Polygon2D.Contains<double>([new(0, 0), new(2, 2)], new Point2D<double>(1, 1.1)));
    }

    /// <summary>Checks orientation scaling avoids overflowing and underflowing determinant products.</summary>
    [Fact]
    public void PolygonOrientationScalesExtremeAndTinyCoordinates()
    {
        Point2D<double>[] huge = [new(-double.MaxValue, -double.MaxValue), new(double.MaxValue, -double.MaxValue), new(double.MaxValue, double.MaxValue), new(-double.MaxValue, double.MaxValue)];
        Assert.True(Polygon2D.Contains<double>(huge, new Point2D<double>(0, 0)));
        Assert.True(Polygon2D.ContainsEvenOdd<double>(huge, new Point2D<double>(0, 0)));
        Point2D<double>[] tiny = [new(0, 0), new(1e-200, 0), new(1e-200, 1e-200), new(0, 1e-200)];
        Assert.True(Polygon2D.Contains<double>(tiny, new Point2D<double>(5e-201, 5e-201)));
        Assert.False(Polygon2D.Contains<double>(tiny, new Point2D<double>(2e-200, 5e-201)));
        Point2D<Half>[] half = [new((Half)0, (Half)0), new((Half)2, (Half)0), new((Half)0, (Half)2)];
        Assert.True(Polygon2D.Contains<Half>(half, new Point2D<Half>((Half)0.5, (Half)0.5)));
    }
}
