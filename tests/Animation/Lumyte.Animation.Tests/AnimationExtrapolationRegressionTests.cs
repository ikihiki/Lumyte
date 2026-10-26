using System.Numerics;

namespace Lumyte.Animation.Tests;

/// <summary>Verifies that finite timing overshoot preserves constant animation values.</summary>
public sealed class AnimationExtrapolationRegressionTests
{
    /// <summary>Samples standard interpolators through large, valid Bezier timing controls.</summary>
    /// <param name="control">The finite vertical Bezier control.</param>
    [Theory]
    [InlineData(-1e20)]
    [InlineData(1e20)]
    public void ConstantCurvesSurviveExtremeFiniteTimingOvershoot(double control)
    {
        IAnimationTiming timing = AnimationTimings.CubicBezier(0, control, 1, control);
        AssertConstant(1f, AnimationInterpolators.Float, timing);
        AssertConstant(new Vector2(1, float.MaxValue), AnimationInterpolators.Vector2, timing);
        AssertConstant(new Vector3(1, float.MaxValue, float.Epsilon), AnimationInterpolators.Vector3, timing);
        AssertConstant(new Vector4(1, float.MaxValue, float.Epsilon, float.MinValue), AnimationInterpolators.Vector4, timing);
        AssertConstant(Quaternion.Identity, AnimationInterpolators.Quaternion, timing);
        AssertConstant(new Quaternion(0.5f, 0.5f, 0.5f, 0.5f), AnimationInterpolators.Quaternion, timing);
    }

    private static void AssertConstant<T>(T value, IAnimationInterpolator<T> interpolator, IAnimationTiming timing)
    {
        var duration = Duration.FromTicks(2);
        AnimationKey<T>[] keys = [new(Duration.Zero, value) { Timing = timing }, new(duration, value)];
        var curve = new AnimationCurve<T>(duration, keys, interpolator);
        Assert.Equal(value, curve.Sample(Duration.Zero));
        Assert.Equal(value, curve.Sample(Duration.FromTicks(1)));
        Assert.Equal(value, curve.Sample(duration));
    }
}
