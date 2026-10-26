using System.Numerics;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Verifies numerical contracts without referencing animation.</summary>
public sealed class InterpolationTests
{
    /// <summary>Checks inverse-x evaluation with an analytically invertible curve.</summary>
    [Fact]
    public void BezierInvertsHorizontalCoordinate()
    {
        var curve = new CubicBezierTiming(0, 1, 0, 1);
        Assert.Equal(0.875, curve.Transform(0.125), 12);
        Assert.Equal(0, curve.Transform(0));
        Assert.Equal(1, curve.Transform(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Transform(double.NaN));
    }

    /// <summary>Checks spatial control points independently from temporal easing.</summary>
    [Fact]
    public void SpatialBezierUsesPointInterpolation()
    {
        Vector2 result = BezierInterpolation.Cubic(Vector2.Zero, Vector2.UnitY, Vector2.One, Vector2.UnitX, 0.5f, Vector2.Lerp);
        Assert.Equal(new Vector2(0.5f, 0.75f), result);
    }

    /// <summary>Checks derivative scaling in arbitrary interval units.</summary>
    [Fact]
    public void HermiteScalesTangentsByInterval()
    {
        Assert.Equal(0.75f, HermiteInterpolation.Interpolate(0f, 1f, 1f, 0f, 2, 0.5f));
        Assert.Equal(1f, HermiteInterpolation.Interpolate(0f, 1f, 1f, 0f, 4, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => HermiteInterpolation.Interpolate(0f, 1f, 0f, 0f, 0, 0.5f));
    }

    /// <summary>Checks normalization against overflow and underflow at double limits.</summary>
    [Fact]
    public void QuaternionNormalizationScalesComponents()
    {
        Assert.Equal(new Quaternion(0.5f, 0.5f, 0.5f, 0.5f), QuaternionInterpolation.Normalize(double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue));
        Assert.Equal(new Quaternion(0, 0, 1, 0), QuaternionInterpolation.Normalize(0, 0, double.Epsilon, 0));
        Assert.Throws<InvalidOperationException>(() => QuaternionInterpolation.Normalize(0, 0, 0, 0));
        Assert.Throws<InvalidOperationException>(() => QuaternionInterpolation.Normalize(double.NaN, 0, 0, 1));
    }

    /// <summary>Checks precise scalar interpolation at opposite float limits.</summary>
    [Fact]
    public void ScalarInterpolationAvoidsIntermediateOverflow()
    {
        Assert.Equal(0, Interpolation.Linear(float.MinValue, float.MaxValue, 0.5f));
        Assert.Equal(0.25, Interpolation.EaseIn(0.5));
        Assert.Equal(0.75, Interpolation.EaseOut(0.5));
        Assert.Equal(0.5, Interpolation.EaseInOut(0.5));
    }
}
