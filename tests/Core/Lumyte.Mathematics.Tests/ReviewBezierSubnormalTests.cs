using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Regressions for review comment 4552218565: interpolation must not discard subnormal coordinates.</summary>
public sealed class ReviewBezierSubnormalTests
{
    /// <summary>Every supported IEEE storage type retains constant and unequal tiny coordinates.</summary>
    /// <param name="precision">The IEEE storage type.</param>
    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    [InlineData("Half")]
    public void IeeeSubnormalCoordinatesSurviveEvaluation(string precision)
    {
        if (precision == "double")
        {
            CheckEvaluation(double.Epsilon, double.MaxValue);
        }
        else if (precision == "float")
        {
            CheckEvaluation(float.Epsilon, float.MaxValue);
        }
        else
        {
            CheckEvaluation(Half.Epsilon, Half.MaxValue);
        }
    }

    /// <summary>Nonflat curves really subdivide while retaining a constant tiny coordinate.</summary>
    /// <param name="precision">The IEEE storage type.</param>
    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    [InlineData("Half")]
    public void NonflatSubdivisionRetainsSubnormalAxis(string precision)
    {
        if (precision == "double")
        {
            CheckSubdivision(double.Epsilon);
        }
        else if (precision == "float")
        {
            CheckSubdivision(float.Epsilon);
        }
        else
        {
            CheckSubdivision(Half.Epsilon);
        }
    }

    /// <summary>The exact decimal branch keeps its existing smallest-coordinate behavior.</summary>
    [Fact]
    public void DecimalTinyConstantRemainsExact()
    {
        var point = new Point2D<decimal>(0.0000000000000000000000000001m, 0);
        Assert.Equal(point, Geometry2D.EvaluateQuadraticBezier(point, point, point, 0.5m));
        Assert.Equal(point, Geometry2D.EvaluateCubicBezier(point, point, point, point, 0.5m));
    }

    /// <summary>Unequal subnormals require more than an equal-component shortcut.</summary>
    [Fact]
    public void UnequalSubnormalCoordinatesRoundOnlyAfterWeighting()
    {
        CheckUnequal(double.Epsilon);
        CheckUnequal(float.Epsilon);
        CheckUnequal(Half.Epsilon);
    }

    /// <summary>Ordinary IEEE inputs retain the established weighted-operation rounding.</summary>
    [Fact]
    public void OrdinaryCoordinatesRetainWeightedRounding()
    {
        var start = new Point2D<double>(0.1, 100.3);
        var control = new Point2D<double>(1.7, -9.8);
        var end = new Point2D<double>(2.9, 21.4);
        double amount = 0.3;
        double inverse = 1 - amount;
        var first = new Point2D<double>((start.X * inverse) + (control.X * amount), (start.Y * inverse) + (control.Y * amount));
        var second = new Point2D<double>((control.X * inverse) + (end.X * amount), (control.Y * inverse) + (end.Y * amount));
        var expected = new Point2D<double>((first.X * inverse) + (second.X * amount), (first.Y * inverse) + (second.Y * amount));
        Assert.Equal(expected, Geometry2D.EvaluateQuadraticBezier(start, control, end, amount));
    }

    /// <summary>Reference-containing decimal-backed numbers also preserve a constant coordinate.</summary>
    [Fact]
    public void ManagedTinyConstantRemainsExact()
    {
        Type scalar = typeof(CustomNumberGeometryTests).GetNestedType("ManagedDecimal", System.Reflection.BindingFlags.NonPublic)!;
        System.Reflection.MethodInfo method = typeof(ReviewBezierSubnormalTests).GetMethod(nameof(CheckManagedConstant), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        method.MakeGenericMethod(scalar).Invoke(null, null);
    }

    private static void CheckManagedConstant<T>()
        where T : INumber<T>
    {
        var point = new Point2D<T>(T.CreateChecked(0.0000000000000000000000000001m), T.Zero);
        T amount = T.CreateChecked(0.5m);
        Assert.Equal(point, Geometry2D.EvaluateQuadraticBezier(point, point, point, amount));
        Assert.Equal(point, Geometry2D.EvaluateCubicBezier(point, point, point, point, amount));
    }

    private static void CheckUnequal<T>(T epsilon)
        where T : IFloatingPointIeee754<T>
    {
        T half = T.One / T.CreateChecked(2);
        var first = new Point2D<T>(epsilon, -epsilon);
        var third = new Point2D<T>(T.CreateChecked(3) * epsilon, -T.CreateChecked(3) * epsilon);
        var expected = new Point2D<T>(T.CreateChecked(2) * epsilon, -T.CreateChecked(2) * epsilon);
        Assert.Equal(expected, Geometry2D.EvaluateQuadraticBezier(first, third, first, half));
        Assert.Equal(expected, Geometry2D.EvaluateCubicBezier(first, third, first, third, half));
    }

    private static void CheckEvaluation<T>(T epsilon, T maximum)
        where T : IFloatingPointIeee754<T>
    {
        T half = T.One / T.CreateChecked(2);
        var point = new Point2D<T>(epsilon, -epsilon);

        // Include exact endpoints and unequal weights, not just midpoint interpolation.
        foreach (T amount in new[] { T.Zero, epsilon, T.CreateChecked(0.25), half, T.CreateChecked(0.75), T.BitDecrement(T.One), T.One })
        {
            Assert.Equal(point, Geometry2D.EvaluateQuadraticBezier(point, point, point, amount));
            Assert.Equal(point, Geometry2D.EvaluateCubicBezier(point, point, point, point, amount));
            var start = new Point2D<T>(epsilon, T.Zero);
            var end = new Point2D<T>(epsilon, T.CreateChecked(4));
            Assert.Equal(new(epsilon, T.CreateChecked(4) * amount), Geometry2D.EvaluateQuadraticBezier(start, new(epsilon, T.CreateChecked(2)), end, amount));
        }

        // Each weighted product rounds separately to an even subnormal on the old path.
        var first = new Point2D<T>(epsilon, -epsilon);
        var third = new Point2D<T>(T.CreateChecked(3) * epsilon, -T.CreateChecked(3) * epsilon);
        var expected = new Point2D<T>(T.CreateChecked(2) * epsilon, -T.CreateChecked(2) * epsilon);
        Assert.Equal(expected, Geometry2D.EvaluateQuadraticBezier(first, third, first, half));
        Assert.Equal(expected, Geometry2D.EvaluateCubicBezier(first, third, first, third, half));
        Assert.Equal(first, Geometry2D.EvaluateQuadraticBezier(first, third, third, T.Zero));
        Assert.Equal(third, Geometry2D.EvaluateCubicBezier(first, first, first, third, T.One));

        // An end-minus-start rewrite would overflow, even though the midpoint is finite.
        var extreme = new Point2D<T>(maximum, -maximum);
        var opposite = new Point2D<T>(-maximum, maximum);
        Assert.Equal(default, Geometry2D.EvaluateQuadraticBezier(extreme, opposite, extreme, half));
        Assert.Equal(default, Geometry2D.EvaluateCubicBezier(extreme, extreme, opposite, opposite, half));
    }

    private static void CheckSubdivision<T>(T epsilon)
        where T : IFloatingPointIeee754<T>
    {
        var start = new Point2D<T>(epsilon, T.Zero);
        var high = new Point2D<T>(epsilon, T.CreateChecked(4));
        var quadratic = new List<Point2D<T>>();
        var cubic = new List<Point2D<T>>();
        T tolerance = T.CreateChecked(0.125);
        Assert.True(Geometry2D.TryFlattenQuadraticBezier(start, high, start, tolerance, quadratic, 10, 1024));
        Assert.True(Geometry2D.TryFlattenCubicBezier(start, high, high, start, tolerance, cubic, 10, 1024));
        Assert.True(quadratic.Count > 2);
        Assert.True(cubic.Count > 2);
        Assert.All(quadratic, point => Assert.Equal(epsilon, point.X));
        Assert.All(cubic, point => Assert.Equal(epsilon, point.X));
    }
}
