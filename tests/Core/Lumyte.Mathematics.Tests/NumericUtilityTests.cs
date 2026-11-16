using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks exact predicates and explicit arithmetic choices for numeric helpers.</summary>
public sealed class NumericUtilityTests
{
    /// <summary>Checks polygons beyond all floating-point ranges and unsigned subtraction.</summary>
    [Fact]
    public void ExactPolygonAndWindingSupportLargeAndUnsignedIntegers()
    {
        var n = BigInteger.Pow(10, 400);
        Point2D<BigInteger>[] square = [new(n, n), new(n + 10, n), new(n + 10, n + 10), new(n, n + 10)];
        Assert.True(Geometry2D.ContainsPolygonClosed<BigInteger>(square, new(n + 5, n + 5)));
        Assert.True(Geometry2D.ContainsPolygonEvenOddClosed<BigInteger>(square, new(n, n + 5)));
        Assert.False(Geometry2D.ContainsPolygonClosed<BigInteger>(square, new(n + 11, n + 5)));
        Assert.Equal(1, Geometry2D.GetWindingContributionFused(n + 10, n, n + 10, n + 10, n + 5, n + 5));
        Point2D<ulong>[] unsigned = [new(0, 0), new(ulong.MaxValue, 0), new(ulong.MaxValue, ulong.MaxValue), new(0, ulong.MaxValue)];
        Assert.True(Geometry2D.ContainsPolygonClosed<ulong>(unsigned, new(1, 1)));
        Assert.Equal(-1, Geometry2D.GetWindingContributionFused(uint.MaxValue, uint.MaxValue, uint.MaxValue, 0u, 1u, 1u));
        Segment2D<BigInteger>[] edges = [new(new(n + 10, n), new(n + 10, n + 10))];
        Assert.Equal(1, Geometry2D.AccumulateWindingFused<BigInteger>(edges, new(n + 5, n + 5)));
    }

    /// <summary>Checks decimal orientation without overflowing products or narrowing tiny differences.</summary>
    [Fact]
    public void DecimalPredicatesRetainExactCoordinates()
    {
        decimal n = 1000000000000000000000000000m;
        Point2D<decimal>[] square = [new(n, n), new(n + 1, n), new(n + 1, n + 1), new(n, n + 1)];
        Assert.True(Geometry2D.ContainsPolygonClosed<decimal>(square, new(n + 0.1m, n + 0.1m)));
        Assert.Equal(1, Geometry2D.GetWindingContributionFused(n + 1, n, n + 1, n + 1, n + 0.1m, n + 0.1m));
        Assert.Equal(1, Geometry2D.GetWindingContributionFused(decimal.MaxValue, 0m, decimal.MaxValue, decimal.MaxValue, 0m, 1m));
    }

    /// <summary>Checks fractional matrices never silently turn integer coefficients into zero.</summary>
    [Fact]
    public void AffineArithmeticRequiresAnExplicitFractionalResult()
    {
        var p = new Point2D<int>(3, 5);
        var matrix = Matrix3x2.CreateScale(0.5f);
        Assert.Throws<ArithmeticException>(() => Geometry2D.TransformAffineFused(p, matrix));
        Assert.Equal(new Point2D<double>(1.5, 2.5), Geometry2D.TransformAffineFused<int, double>(p, matrix));
        Assert.Equal(new Point2D<decimal>(1.5m, 2.5m), Geometry2D.TransformAffineFused<int, decimal>(p, matrix));
        Assert.Equal(new Point2D<int>(7, 17), Geometry2D.TransformAffineChecked(p, 2, 0, 0, 3, 1, 2));
        Assert.Throws<OverflowException>(() => Geometry2D.TransformAffineChecked(new Point2D<int>(int.MaxValue, 0), 2, 0, 0, 1, 0, 0));
        decimal x = 1.000000000000000000000000001m;
        Assert.Equal(x * 0.5m, Geometry2D.TransformAffineChecked(new Point2D<decimal>(x, 0), 0.5m, 0m, 0m, 1m, 0m, 0m).X);
    }

    /// <summary>Checks native decimal evaluation and explicit integer-to-fractional evaluation.</summary>
    [Fact]
    public void BezierEvaluationUsesSelectedArithmetic()
    {
        var start = new Point2D<int>(0, 0);
        var control = new Point2D<int>(0, 1);
        var end = new Point2D<int>(1, 0);
        Assert.Equal(start, Geometry2D.EvaluateQuadraticBezier(start, control, end, 0));
        Assert.Equal(end, Geometry2D.EvaluateCubicBezier(start, control, control, end, 1));
        Assert.Equal(new Point2D<decimal>(0.25m, 0.5m), Geometry2D.EvaluateQuadraticBezier<int, decimal>(start, control, end, 0.5m));
        Assert.Equal(new Point2D<double>(0.125, 0.75), Geometry2D.EvaluateCubicBezier<int, double>(start, control, control, end, 0.5));
        decimal amount = 0.500000000000000000000000001m;
        Assert.Equal(amount, Geometry2D.EvaluateQuadraticBezier(new Point2D<decimal>(0, 0), new(0.5m, 0), new(1, 0), amount).X);
        var maximum = new Point2D<decimal>(decimal.MaxValue, decimal.MaxValue);
        var minimum = new Point2D<decimal>(decimal.MinValue, decimal.MinValue);
        Assert.Equal(new Point2D<decimal>(0m, 0m), Geometry2D.EvaluateQuadraticBezier(maximum, minimum, maximum, 0.5m));
        Assert.Equal(new Point2D<decimal>(0m, 0m), Geometry2D.EvaluateCubicBezier(maximum, minimum, maximum, minimum, 0.5m));
    }

    /// <summary>Checks integer flattening rejects truncated halves and real result overloads produce a polyline.</summary>
    [Fact]
    public void FlatteningCanChooseFractionalOutputTransactionally()
    {
        var start = new Point2D<int>(0, 0);
        var control = new Point2D<int>(1, 2);
        var end = new Point2D<int>(2, 0);
        var integers = new List<Point2D<int>> { end };
        Assert.Throws<ArithmeticException>(() => Geometry2D.TryFlattenQuadraticBezier(start, control, end, 1, integers, 12, 1024));
        Assert.Single(integers);
        var points = new List<Point2D<decimal>>();
        Assert.True(Geometry2D.TryFlattenQuadraticBezier<int, decimal>(start, control, end, 0.01m, points, 12, 1024));
        Assert.Equal(new Point2D<decimal>(0, 0), points[0]);
        Assert.Equal(new Point2D<decimal>(2, 0), points[^1]);
        Assert.Contains(points, p => p.X % 1 != 0);
        var cubic = new List<Point2D<double>>();
        Assert.True(Geometry2D.TryFlattenCubicBezier<int, double>(start, control, control, end, 0.01, cubic, 12, 1024));
        Assert.True(cubic.Count > 2);
        int count = points.Count;
        Assert.False(Geometry2D.TryFlattenCubicBezier<int, decimal>(start, control, control, end, 0.01m, points, 0, 2));
        Assert.Equal(count, points.Count);
    }

    /// <summary>Checks constant decimal curves at both storage extremes and exact endpoints.</summary>
    /// <param name="text">The invariant decimal coordinate.</param>
    [Theory]
    [InlineData("79228162514264337593543950335")]
    [InlineData("0.0000000000000000000000000001")]
    public void DecimalBezierPreservesConstantCurves(string text)
    {
        decimal value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var point = new Point2D<decimal>(value, -value);
        Assert.Equal(point, Geometry2D.EvaluateQuadraticBezier(point, point, point, 0.5m));
        Assert.Equal(point, Geometry2D.EvaluateCubicBezier(point, point, point, point, 0.5m));
        var zero = new Point2D<decimal>(0m, 0m);
        Assert.Equal(point, Geometry2D.EvaluateQuadraticBezier(point, zero, zero, 0m));
        Assert.Equal(point, Geometry2D.EvaluateCubicBezier(zero, zero, zero, point, 1m));
    }

    /// <summary>Checks flat integral curves do not need fractional subdivision or square roots.</summary>
    [Fact]
    public void IntegralFlatnessUsesExactSquaredDistanceBeforeSubdivision()
    {
        var quadratic = new List<Point2D<int>>();
        Assert.True(Geometry2D.TryFlattenQuadraticBezier(new(0, 0), new(1, 0), new(2, 0), 1, quadratic, 0, 2));
        Assert.Equal(new Point2D<int>[] { new(0, 0), new(2, 0) }, quadratic);
        var cubic = new List<Point2D<int>>();
        Assert.True(Geometry2D.TryFlattenCubicBezier(new(0, 0), new(1, 0), new(2, 0), new(3, 0), 1, cubic, 0, 2));
        Assert.Equal(2, cubic.Count);

        // The interior projection has distance sqrt(1/2), which is below one.
        quadratic.Clear();
        Assert.True(Geometry2D.TryFlattenQuadraticBezier(new(0, 0), new(0, 1), new(2, 2), 1, quadratic, 0, 2));
        Assert.Equal(2, quadratic.Count);
        cubic.Clear();
        Assert.True(Geometry2D.TryFlattenCubicBezier(new(0, 0), new(0, 1), new(1, 2), new(2, 2), 1, cubic, 0, 2));
        Assert.Equal(2, cubic.Count);

        quadratic.Clear();
        Assert.True(Geometry2D.TryFlattenQuadraticBezier(new(0, 0), new(1, 1), new(0, 0), 2, quadratic, 0, 2));
        Assert.Equal(2, quadratic.Count);
        quadratic.Clear();
        Assert.True(Geometry2D.TryFlattenQuadraticBezier(new(0, 0), new(2, 1), new(1, 0), 2, quadratic, 0, 2));
        Assert.Equal(2, quadratic.Count);

        quadratic.Clear();
        Assert.False(Geometry2D.TryFlattenQuadraticBezier(new(0, 0), new(0, 3), new(2, 2), 1, quadratic, 0, 2));
        Assert.Empty(quadratic);
        Assert.Throws<ArithmeticException>(() => Geometry2D.TryFlattenQuadraticBezier(new(0, 0), new(0, 3), new(2, 2), 1, quadratic, 2, 8));
        Assert.Empty(quadratic);
    }
}
