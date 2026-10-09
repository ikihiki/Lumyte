using System.Numerics;
using Lumyte.Core.Time;
using Xunit;

namespace Lumyte.Animation.Tests;

/// <summary>Checks numeric stability and exact time boundaries of interpolation.</summary>
public sealed class AnimationInterpolationTests
{
    /// <summary>Keeps discrete values until the exact endpoint for every easing.</summary>
    /// <param name="easing">The easing under test.</param>
    [Theory]
    [InlineData(AnimationEasing.Linear)]
    [InlineData(AnimationEasing.EaseIn)]
    [InlineData(AnimationEasing.EaseOut)]
    [InlineData(AnimationEasing.EaseInOut)]
    public void StepTweenChangesOnlyAtExactEndpoint(AnimationEasing easing)
    {
        foreach (long ticks in new[] { 2L, TimeSpan.TicksPerSecond, 10 * TimeSpan.TicksPerSecond, long.MaxValue })
        {
            var duration = Duration.FromTicks(ticks);
            var tween = new Tween<string>("before", "after", duration, AnimationInterpolators.Step<string>(), easing);
            Assert.Equal("before", tween.Sample(Duration.Zero));
            Assert.Equal("before", tween.Sample(Duration.FromTicks(1)));
            Assert.Equal("before", tween.Sample(Duration.FromTicks(ticks - 1)));
            Assert.Equal("after", tween.Sample(duration));
        }

        var nearEnd = new Tween<bool>(false, true, Duration.FromSeconds(1), AnimationInterpolators.Step<bool>(), easing);
        Assert.False(nearEnd.Sample(Duration.FromSeconds(0.9999)));
        Assert.True(nearEnd.Sample(Duration.FromSeconds(1)));
    }

    /// <summary>Passes strictly interior amounts to custom interpolation before endpoints.</summary>
    /// <param name="easing">The easing under test.</param>
    [Theory]
    [InlineData(AnimationEasing.Linear)]
    [InlineData(AnimationEasing.EaseIn)]
    [InlineData(AnimationEasing.EaseOut)]
    [InlineData(AnimationEasing.EaseInOut)]
    public void TweenKeepsInteriorAmountsBetweenEndpoints(AnimationEasing easing)
    {
        var duration = Duration.FromTicks(long.MaxValue);
        var tween = new Tween<float>(-1, 2, duration, new AmountInterpolator(), easing);
        Assert.Equal(-1, tween.Sample(Duration.Zero));
        Assert.InRange(tween.Sample(Duration.FromTicks(1)), float.Epsilon, MathF.BitDecrement(1));
        Assert.InRange(tween.Sample(Duration.FromTicks(long.MaxValue - 1)), float.Epsilon, MathF.BitDecrement(1));
        Assert.Equal(2, tween.Sample(duration));
    }

    /// <summary>Preserves exact intermediate and final key boundaries at large time values.</summary>
    [Fact]
    public void StepCurveChangesOnlyAtExactKeyTimes()
    {
        var middle = Duration.FromSeconds(10);
        var end = Duration.FromTicks(long.MaxValue - 1);
        AnimationKey<string>[] keys =
        [
            new(Duration.Zero, "first"),
            new(middle, "middle"),
            new(end, "last"),
        ];
        var curve = new AnimationCurve<string>(Duration.FromTicks(long.MaxValue), keys, AnimationInterpolators.Step<string>());

        Assert.Equal("first", curve.Sample(Duration.Zero));
        Assert.Equal("first", curve.Sample(Duration.FromTicks(middle.Ticks - 1)));
        Assert.Equal("middle", curve.Sample(middle));
        Assert.Equal("middle", curve.Sample(Duration.FromTicks(middle.Ticks + 1)));
        Assert.Equal("middle", curve.Sample(Duration.FromTicks(end.Ticks - 1)));
        Assert.Equal("last", curve.Sample(end));
        Assert.Equal("last", curve.Sample(curve.Duration));
    }

    /// <summary>Returns exact key values without invoking the custom interpolator.</summary>
    [Fact]
    public void CurveReturnsStoredValuesAtExactKeys()
    {
        AnimationKey<float>[] keys =
        [
            new(Duration.FromTicks(1), 10),
            new(Duration.FromTicks(2), 20),
            new(Duration.FromTicks(3), 30),
        ];
        var curve = new AnimationCurve<float>(Duration.FromTicks(4), keys, new AmountInterpolator());

        Assert.Equal(10, curve.Sample(Duration.Zero));
        Assert.Equal(10, curve.Sample(Duration.FromTicks(1)));
        Assert.Equal(20, curve.Sample(Duration.FromTicks(2)));
        Assert.Equal(30, curve.Sample(Duration.FromTicks(3)));
        Assert.Equal(30, curve.Sample(Duration.FromTicks(4)));
    }

    /// <summary>Interpolates opposite extreme finite values without overflowing.</summary>
    /// <param name="amount">The normalized interpolation amount.</param>
    /// <param name="expected">The expected value at that fraction of the range.</param>
    [Theory]
    [InlineData(0f, float.MinValue)]
    [InlineData(0.25f, float.MinValue / 2)]
    [InlineData(0.5f, 0f)]
    [InlineData(0.75f, float.MaxValue / 2)]
    [InlineData(1f, float.MaxValue)]
    public void FloatInterpolatorKeepsFiniteExtremeValues(float amount, float expected)
    {
        float forward = AnimationInterpolators.Float.Interpolate(float.MinValue, float.MaxValue, amount);
        float reverse = AnimationInterpolators.Float.Interpolate(float.MaxValue, float.MinValue, amount);
        Assert.True(float.IsFinite(forward));
        Assert.Equal(expected, forward);
        Assert.Equal(-expected, reverse);
        Assert.Equal(float.MaxValue, AnimationInterpolators.Float.Interpolate(float.MaxValue, float.MaxValue, amount));
        Assert.Equal(float.MinValue, AnimationInterpolators.Float.Interpolate(float.MinValue, float.MinValue, amount));
    }

    /// <summary>Preserves tiny endpoints next to values with a much larger magnitude.</summary>
    [Fact]
    public void FloatInterpolatorPreservesEndpointsWithDifferentMagnitudes()
    {
        Assert.Equal(float.Epsilon, AnimationInterpolators.Float.Interpolate(float.Epsilon, float.MaxValue, 0));
        Assert.Equal(float.Epsilon, AnimationInterpolators.Float.Interpolate(float.MaxValue, float.Epsilon, 1));
        Assert.Equal(-float.Epsilon, AnimationInterpolators.Float.Interpolate(-float.Epsilon, float.MinValue, 0));
        Assert.Equal(-float.Epsilon, AnimationInterpolators.Float.Interpolate(float.MinValue, -float.Epsilon, 1));
    }

    /// <summary>Samples finite extreme values through both supported scalar sources.</summary>
    [Fact]
    public void TweenAndCurveInterpolateFiniteExtremeValues()
    {
        var duration = Duration.FromSeconds(10);
        var tween = new Tween<float>(float.MinValue, float.MaxValue, duration, AnimationInterpolators.Float, AnimationEasing.Linear);
        AnimationKey<float>[] keys =
        [
            new(Duration.Zero, float.MinValue),
            new(duration, float.MaxValue),
        ];
        var curve = new AnimationCurve<float>(duration, keys, AnimationInterpolators.Float);

        Assert.Equal(0, tween.Sample(Duration.FromSeconds(5)));
        Assert.Equal(0, curve.Sample(Duration.FromSeconds(5)));
        Assert.Equal(float.MinValue, tween.Sample(Duration.Zero));
        Assert.Equal(float.MaxValue, tween.Sample(duration));
        Assert.Equal(float.MinValue, curve.Sample(Duration.Zero));
        Assert.Equal(float.MaxValue, curve.Sample(duration));
    }

    /// <summary>Checks the remaining standard numeric interpolators at extreme and rotation values.</summary>
    [Fact]
    public void VectorAndQuaternionInterpolationRemainFinite()
    {
        Assert.Equal(Vector2.Zero, AnimationInterpolators.Vector2.Interpolate(new(float.MinValue), new(float.MaxValue), 0.5f));
        Assert.Equal(Vector3.Zero, AnimationInterpolators.Vector3.Interpolate(new(float.MinValue), new(float.MaxValue), 0.5f));
        Assert.Equal(Vector4.Zero, AnimationInterpolators.Vector4.Interpolate(new(float.MinValue), new(float.MaxValue), 0.5f));
        Assert.Equal(new Vector2(float.MaxValue), AnimationInterpolators.Vector2.Interpolate(new(float.MaxValue), new(float.MaxValue), 0.5f));
        Assert.Equal(new Vector3(float.MaxValue), AnimationInterpolators.Vector3.Interpolate(new(float.MaxValue), new(float.MaxValue), 0.5f));
        Assert.Equal(new Vector4(float.MaxValue), AnimationInterpolators.Vector4.Interpolate(new(float.MaxValue), new(float.MaxValue), 0.5f));

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        Quaternion middle = AnimationInterpolators.Quaternion.Interpolate(Quaternion.Identity, rotation, 0.5f);
        Assert.Equal(1, middle.Length(), 5);
        Assert.Equal(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4).Y, middle.Y, 5);
    }

    private sealed class AmountInterpolator : IAnimationInterpolator<float>
    {
        /// <summary>Returns the supplied amount to expose its boundary behavior.</summary>
        /// <param name="from">The starting value.</param>
        /// <param name="to">The ending value.</param>
        /// <param name="amount">The interpolation amount.</param>
        /// <returns>The unmodified normalized amount.</returns>
        public float Interpolate(float from, float to, float amount) => amount;
    }
}
