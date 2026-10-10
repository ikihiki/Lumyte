using System.Numerics;
using Lumyte.Core.Time;
using Xunit;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Tests;

/// <summary>Checks format-independent timing, tangent interpolation and composed value sources.</summary>
public sealed class AnimationFormatPrimitiveTests
{
    /// <summary>Checks time Bezier inversion against analytically known x=t^3 and y=1-(1-t)^3.</summary>
    [Fact]
    public void TemporalBezierInvertsXInsteadOfUsingProgressAsParameter()
    {
        IAnimationTiming timing = AnimationTimings.CubicBezier(0, 1, 0, 1);
        Assert.Equal(0, timing.Transform(0));
        Assert.Equal(1, timing.Transform(1));
        Assert.Equal(0.875, timing.Transform(0.125), precision: 12);
        Assert.Equal(0.984375, timing.Transform(0.421875), precision: 12);
        IAnimationTiming flatDerivative = AnimationTimings.CubicBezier(1, 0, 0, 1);
        Assert.Equal(0.5, flatDerivative.Transform(0.5), precision: 12);
        Assert.True(flatDerivative.Transform(0.49) < 0.5);
        Assert.True(flatDerivative.Transform(0.51) > 0.5);
    }

    /// <summary>Checks easing overshoot, shared polynomial timing, invalid controls and invalid progress.</summary>
    [Fact]
    public void TimingAllowsFiniteOvershootAndRejectsInvalidInput()
    {
        Assert.Equal(1.625, AnimationTimings.CubicBezier(0, 2, 1, 2).Transform(0.5), precision: 12);
        Assert.Equal(0.25, AnimationTimings.EaseIn.Transform(0.5));
        Assert.Equal(0.75, AnimationTimings.EaseOut.Transform(0.5));
        Assert.Equal(0.125, AnimationTimings.EaseInOut.Transform(0.25));
        Assert.Equal(0.25, AnimationTimings.Linear.Transform(0.25));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationTimings.CubicBezier(-0.1, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationTimings.CubicBezier(0, 0, 1.1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationTimings.CubicBezier(0, double.NaN, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationTimings.Linear.Transform(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationTimings.Linear.Transform(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationTimings.Linear.Transform(1.1));
    }

    /// <summary>Checks segment Hold, easing, override interpolation, exact keys and copied key storage.</summary>
    [Fact]
    public void CurveUsesOutgoingSegmentSettingsWithoutChangingKeyBoundaries()
    {
        AnimationKey<float>[] keys =
        [
            new(Ticks(2), 0) { Hold = true, Timing = new ThrowingTiming() },
            new(Ticks(6), 10) { Timing = AnimationTimings.EaseIn },
            new(Ticks(10), 20) { Interpolator = AnimationInterpolators.CubicBezier(30f, 30f, AnimationInterpolators.Float) },
            new(Ticks(14), 40),
        ];
        var curve = new AnimationCurve<float>(Ticks(16), keys, AnimationInterpolators.Float);
        keys[1] = new(Ticks(6), 999);
        Assert.Equal(0, curve.Sample(Duration.Zero));
        Assert.Equal(0, curve.Sample(Ticks(5)));
        Assert.Equal(10, curve.Sample(Ticks(6)));
        Assert.Equal(12.5f, curve.Sample(Ticks(8)));
        Assert.Equal(20, curve.Sample(Ticks(10)));
        Assert.Equal(30, curve.Sample(Ticks(12)));
        Assert.Equal(40, curve.Sample(Ticks(16)));
        (Duration time, float value) = keys[0];
        Assert.Equal(Ticks(2), time);
        Assert.Equal(0, value);
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Sample(Ticks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Sample(Ticks(17)));
    }

    /// <summary>Checks overshoot is preserved while non-finite custom easing is rejected.</summary>
    [Fact]
    public void CurvePreservesOvershootAndRejectsNonFiniteTimingResults()
    {
        AnimationKey<float>[] keys = [new(Duration.Zero, 0) { Timing = AnimationTimings.CubicBezier(0, 2, 1, 2) }, new(Ticks(10), 10)];
        var curve = new AnimationCurve<float>(Ticks(10), keys, AnimationInterpolators.Float);
        Assert.Equal(16.25f, curve.Sample(Ticks(5)));
        var overflow = new AnimationCurve<float>(Ticks(10), [keys[0], new(Ticks(10), float.MaxValue)], AnimationInterpolators.Float);
        Assert.Throws<InvalidOperationException>(() => overflow.Sample(Ticks(5)));
        keys[0] = new(Duration.Zero, 0) { Timing = new NonFiniteTiming() };
        var invalid = new AnimationCurve<float>(Ticks(10), keys, AnimationInterpolators.Float);
        Assert.Throws<InvalidOperationException>(() => invalid.Sample(Ticks(5)));
        Assert.Equal(0, invalid.Sample(Duration.Zero));
        Assert.Equal(10, invalid.Sample(Ticks(10)));
    }

    /// <summary>Checks spatial Bezier evaluation and temporal easing remain independent.</summary>
    [Fact]
    public void SpatialBezierCalculatesPointsAndPreservesExactEndpoints()
    {
        IAnimationInterpolator<Vector2> spatial = AnimationInterpolators.CubicBezier(new Vector2(0, 1), new Vector2(1, 1), AnimationInterpolators.Vector2);
        Assert.Equal(Vector2.Zero, spatial.Interpolate(Vector2.Zero, Vector2.UnitX, 0));
        Assert.Equal(Vector2.UnitX, spatial.Interpolate(Vector2.Zero, Vector2.UnitX, 1));
        Assert.Equal(new Vector2(0.5f, 0.75f), spatial.Interpolate(Vector2.Zero, Vector2.UnitX, 0.5f));
        var curve = new AnimationCurve<Vector2>(Ticks(4), [new(Duration.Zero, Vector2.Zero) { Timing = AnimationTimings.EaseIn, Interpolator = spatial }, new(Ticks(4), Vector2.UnitX)], AnimationInterpolators.Vector2);
        Assert.Equal(spatial.Interpolate(Vector2.Zero, Vector2.UnitX, 0.25f), curve.Sample(Ticks(2)));
    }

    /// <summary>Checks Hermite tangents use seconds and work over nonuniform intervals.</summary>
    [Fact]
    public void HermiteTangentsAreScaledByEachKeyIntervalInSeconds()
    {
        AnimationHermiteKey<float>[] keys =
        [
            new(Duration.FromSeconds(1), 0, 0, 2),
            new(Duration.FromSeconds(3), 0, 0, 4),
            new(Duration.FromSeconds(7), 0, 0, 0),
        ];
        var curve = new AnimationHermiteCurve<float>(Duration.FromSeconds(8), keys, AnimationHermiteInterpolators.Float);
        keys[0] = new(Duration.FromSeconds(1), 999, 0, 0);
        Assert.Equal(0, curve.Sample(Duration.Zero));
        Assert.Equal(0.5f, curve.Sample(Duration.FromSeconds(2)));
        Assert.Equal(0, curve.Sample(Duration.FromSeconds(3)));
        Assert.Equal(2, curve.Sample(Duration.FromSeconds(5)));
        Assert.Equal(0, curve.Sample(Duration.FromSeconds(8)));
        var singleton = new AnimationHermiteCurve<float>(Ticks(10), [new(Ticks(5), 42, 0, 0)], AnimationHermiteInterpolators.Float);
        Assert.Equal(42, singleton.Sample(Duration.Zero));
        Assert.Equal(42, singleton.Sample(Ticks(10)));
    }

    /// <summary>Checks scalar and vector Hermite components against independently known midpoint weights.</summary>
    [Fact]
    public void HermiteStandardComponentsAndEndpointValuesAreCorrect()
    {
        var interval = Duration.FromSeconds(2);
        Assert.Equal(0.5f, AnimationHermiteInterpolators.Float.Interpolate(0, 0, 2, 0, interval, 0.5f));
        Assert.Equal(new Vector2(0.5f), AnimationHermiteInterpolators.Vector2.Interpolate(Vector2.Zero, Vector2.Zero, new(2), Vector2.Zero, interval, 0.5f));
        Assert.Equal(new Vector3(0.5f), AnimationHermiteInterpolators.Vector3.Interpolate(Vector3.Zero, Vector3.Zero, new(2), Vector3.Zero, interval, 0.5f));
        Assert.Equal(new Vector4(0.5f), AnimationHermiteInterpolators.Vector4.Interpolate(Vector4.Zero, Vector4.Zero, new(2), Vector4.Zero, interval, 0.5f));
        Assert.Equal(float.Epsilon, AnimationHermiteInterpolators.Float.Interpolate(float.Epsilon, float.MaxValue, 0, 0, interval, 0));
        Assert.Equal(0, AnimationHermiteInterpolators.Float.Interpolate(float.MinValue, float.MaxValue, 0, 0, interval, 0.5f));
        Assert.Equal(float.MaxValue, AnimationHermiteInterpolators.Float.Interpolate(float.MaxValue, float.MaxValue, 0, 0, interval, 0.0004f));
        Assert.Equal(float.MinValue, AnimationHermiteInterpolators.Float.Interpolate(float.MinValue, float.MinValue, 0, 0, interval, 0.0004f));
        Assert.Equal(1, AnimationHermiteInterpolators.Float.Interpolate(1, 1, float.MaxValue, float.MaxValue, interval, 0.5f));
    }

    /// <summary>Checks zero/nonunit quaternion tangents, component interpolation and output normalization.</summary>
    [Fact]
    public void HermiteQuaternionNormalizesComponentsAndRejectsSingularRotations()
    {
        var end = new Quaternion(0, 0, 1, 0);
        var curve = new AnimationHermiteCurve<Quaternion>(Duration.FromSeconds(2), [new(Duration.Zero, Quaternion.Identity, default, default), new(Duration.FromSeconds(2), end, default, default)], AnimationHermiteInterpolators.Quaternion);
        Quaternion value = curve.Sample(Duration.FromSeconds(1));
        Assert.InRange(value.LengthSquared(), 0.99999f, 1.00001f);
        Assert.InRange(value.Z, 0.70710f, 0.70711f);
        Assert.InRange(value.W, 0.70710f, 0.70711f);
        var nonunit = new AnimationHermiteCurve<Quaternion>(Duration.FromSeconds(2), [new(Duration.Zero, Quaternion.Identity, default, new Quaternion(0, 0, 2, 0)), new(Duration.FromSeconds(2), end, default, default)], AnimationHermiteInterpolators.Quaternion);
        Assert.InRange(nonunit.Sample(Duration.FromSeconds(1)).LengthSquared(), 0.99999f, 1.00001f);
        Quaternion huge = AnimationHermiteInterpolators.Quaternion.Interpolate(Quaternion.Identity, end, new Quaternion(0, 0, float.MaxValue, 0), default, Duration.FromSeconds(100), 0.5f);
        Assert.InRange(huge.LengthSquared(), 0.99999f, 1.00001f);
        var tiny = new AnimationHermiteCurve<Quaternion>(Ticks(2), [new(Duration.Zero, Quaternion.Identity, default, new Quaternion(0, 0, float.Epsilon, 0)), new(Ticks(2), -Quaternion.Identity, default, default)], AnimationHermiteInterpolators.Quaternion);
        Assert.Equal(end, tiny.Sample(Ticks(1)));
        Assert.Equal(Quaternion.Identity, AnimationHermiteInterpolators.Quaternion.Interpolate(Quaternion.Identity, Quaternion.Identity, new Quaternion(0, 0, 0, float.MaxValue), new Quaternion(0, 0, 0, float.MaxValue), interval: Duration.FromSeconds(100), amount: 0.5f));
        Assert.Throws<InvalidOperationException>(() => AnimationHermiteInterpolators.Quaternion.Interpolate(Quaternion.Identity, -Quaternion.Identity, default, default, Duration.FromSeconds(1), 0.5f));
    }

    /// <summary>Checks invalid Hermite duration, keys, tangents, amount and overflowing results.</summary>
    [Fact]
    public void HermiteRejectsInvalidDefinitionsAndNonFiniteResults()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationHermiteCurve<float>(Duration.Zero, [new(Duration.Zero, 1, 0, 0)], AnimationHermiteInterpolators.Float));
        Assert.Throws<ArgumentException>(() => new AnimationHermiteCurve<float>(Ticks(10), [], AnimationHermiteInterpolators.Float));
        Assert.Throws<ArgumentException>(() => new AnimationHermiteCurve<float>(Ticks(10), [new(Ticks(5), 1, 0, 0), new(Ticks(5), 2, 0, 0)], AnimationHermiteInterpolators.Float));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationHermiteCurve<float>(Ticks(10), [new(Ticks(11), 1, 0, 0)], AnimationHermiteInterpolators.Float));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationHermiteCurve<float>(Ticks(10), [new(Duration.Zero, 1, float.NaN, 0)], AnimationHermiteInterpolators.Float));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationHermiteInterpolators.Float.Interpolate(0, 1, 0, 0, Ticks(10), float.NaN));
        Assert.Throws<InvalidOperationException>(() => AnimationHermiteInterpolators.Float.Interpolate(0, 0, float.MaxValue, 0, Duration.FromSeconds(100), 0.5f));
    }

    /// <summary>Checks exact large tick endpoints, reversed mapping, stopped time and invalid mapped positions.</summary>
    [Fact]
    public void TimeRemappingSupportsReverseAndHoldWithIndependentDuration()
    {
        Duration maximum = Ticks(long.MaxValue);
        Assert.Equal(maximum, AnimationInterpolators.Duration.Interpolate(Duration.Zero, maximum, 1));
        Assert.Equal(Duration.Zero, AnimationInterpolators.Duration.Interpolate(maximum, Duration.Zero, 1));
        Assert.Equal(Ticks((long.MaxValue / 2) + 1), AnimationInterpolators.Duration.Interpolate(Duration.Zero, maximum, 0.5f));
        Assert.Equal(Ticks(8), AnimationInterpolators.Duration.Interpolate(Ticks(10), Duration.Zero, 0.2f));
        Assert.Equal(Ticks(long.MaxValue - 1), AnimationInterpolators.Duration.Interpolate(Ticks(long.MaxValue - 1), maximum, 0.5f));
        Assert.Equal(Duration.Zero, AnimationInterpolators.Duration.Interpolate(Duration.Zero, Ticks(1), 0.5f));
        IAnimationSource<float> source = Scalar(0, 10, Ticks(10));
        var map = new AnimationCurve<Duration>(Ticks(20), [new(Duration.Zero, Ticks(10)) { Hold = true }, new(Ticks(10), Ticks(10)), new(Ticks(20), Duration.Zero)], AnimationInterpolators.Duration);
        var remap = new AnimationTimeRemap<float>(source, map);
        Assert.Equal(Ticks(20), remap.Duration);
        Assert.Equal(10, remap.Sample(Ticks(5)));
        Assert.Equal(5, remap.Sample(Ticks(15)));
        Assert.Equal(0, remap.Sample(Ticks(20)));
        Assert.Throws<ArgumentOutOfRangeException>(() => remap.Sample(Ticks(21)));
        var invalid = new AnimationTimeRemap<float>(source, new Tween<Duration>(Ticks(-1), Ticks(-1), Ticks(10), AnimationInterpolators.Duration, AnimationEasing.Linear));
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Sample(Duration.Zero));
    }

    /// <summary>Checks varying blend weights and that zero/one do not evaluate the unused source.</summary>
    [Fact]
    public void BlendUsesChangingWeightsAndShortCircuitsEndpoints()
    {
        var weight = new AnimationCurve<float>(Ticks(10), [new(Duration.Zero, 0), new(Ticks(10), 1)], AnimationInterpolators.Float);
        var blend = new AnimationBlend<float>(Scalar(0, 10), Scalar(10, 20), weight, AnimationInterpolators.Float);
        Assert.Equal(0, blend.Sample(Duration.Zero));
        Assert.Equal(10, blend.Sample(Ticks(5)));
        Assert.Equal(20, blend.Sample(Ticks(10)));
        var unused = new ThrowingSource<float>(Ticks(10));
        Assert.Equal(5, new AnimationBlend<float>(Scalar(0, 10), unused, Scalar(0, 0), AnimationInterpolators.Float).Sample(Ticks(5)));
        Assert.Equal(5, new AnimationBlend<float>(unused, Scalar(0, 10), Scalar(1, 1), AnimationInterpolators.Float).Sample(Ticks(5)));
        Assert.Throws<ArgumentException>(() => new AnimationBlend<float>(Scalar(0, 1), Scalar(0, 1, Ticks(20)), weight, AnimationInterpolators.Float));
        var invalid = new AnimationBlend<float>(Scalar(0, 1), Scalar(1, 2), Scalar(2, 2), AnimationInterpolators.Float);
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Sample(Ticks(5)));
    }

    /// <summary>Checks nested generated definitions build automatically and snapshots survive subsequent editing.</summary>
    [Fact]
    public void CompositionBuildsNestedSourcesWithOneTimelineBuild()
    {
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.Curve<float> motion = Curve<float>(Ticks(10), AnimationInterpolators.Float)[new AnimationKey<float>(Duration.Zero, 0), new AnimationKey<float>(Ticks(10), 10)];
        ComposeAnimation.Definitions.Curve<Duration> map = Curve<Duration>(Ticks(10), AnimationInterpolators.Duration)[new AnimationKey<Duration>(Duration.Zero, Ticks(10)), new AnimationKey<Duration>(Ticks(10), Duration.Zero)];
        ComposeAnimation.Definitions.Sampled<float> weight = Sampled<float>(Scalar(0.5f, 0.5f));
        ComposeAnimation.Definitions.Blend<float> blend = Blend<float>(from: TimeRemap<float>(value: motion, timeMap: map), to: motion, weight: weight, interpolator: AnimationInterpolators.Float);
        AnimationTimeline timeline = Timeline()[SourceTrack<float>(channel, blend)].Build();
        motion.Keys = [new(Duration.Zero, 100), new(Ticks(10), 100)];
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(2));
        playback.Update(output, []);
        Assert.True(output.TryGet(channel, out float result));
        Assert.Equal(5, result);
        IAnimationSource<float> edited = blend.Build();
        Assert.Equal(100, edited.Sample(Ticks(2)));
        ComposeAnimation.Definitions.HermiteCurve<float> hermite = HermiteCurve<float>(Duration.FromSeconds(2), AnimationHermiteInterpolators.Float)[
            new AnimationHermiteKey<float>(Duration.Zero, 0, 0, 2), new AnimationHermiteKey<float>(Duration.FromSeconds(2), 0, 0, 0)];
        Assert.Equal(0.5f, hermite.Build().Sample(Duration.FromSeconds(1)));
    }

    /// <summary>Checks source definition cycles are rejected while shared definitions remain valid.</summary>
    [Fact]
    public void CompositionRejectsSourceCyclesAndAllowsSharedChildren()
    {
        ComposeAnimation.Definitions.Sampled<float> value = Sampled<float>(Scalar(0, 10));
        ComposeAnimation.Definitions.Sampled<float> weight = Sampled<float>(Scalar(0.5f, 0.5f));
        ComposeAnimation.Definitions.Blend<float> blend = Blend<float>(from: value, to: value, weight: weight, interpolator: AnimationInterpolators.Float);
        Assert.Equal(5, blend.Build().Sample(Ticks(5)));
        typeof(ComposeAnimation.Definitions.Blend<float>).GetProperty(nameof(blend.From))!.SetValue(blend, blend);
        Assert.Throws<ArgumentException>(() => blend.Build());
        var channel = AnimationChannel<float>.Create();
        Assert.Throws<ArgumentException>(() => Timeline()[SourceTrack<float>(channel, blend)].Build());
        Assert.Throws<ArgumentOutOfRangeException>(() => Timeline()[SourceTrack<float>(channel, value, (AnimationFillMode)99)].Build());
    }

    /// <summary>Checks computed tracks preserve endpoint Release and finite repetition behavior.</summary>
    [Fact]
    public void SourceTracksUseExistingReleaseAndRepeatContracts()
    {
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.Curve<float> source = Curve<float>(Ticks(5), AnimationInterpolators.Float)[new AnimationKey<float>(Duration.Zero, 0), new AnimationKey<float>(Ticks(5), 5)];
        AnimationTimeline timeline = Timeline()[Repeat(2)[SourceTrack<float>(channel, source, AnimationFillMode.Release)], Delay(Ticks(12))].Build();
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(11));
        playback.Update(output, []);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(5, value);
        output.Clear();
        playback.Update(output, []);
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>Checks warmed scalar, vector and quaternion source evaluation introduces no managed allocation.</summary>
    [Fact]
    public void WarmedNewNumericSourcesAllocateNoManagedMemory()
    {
        var duration = Duration.FromSeconds(2);
        var scalar = new AnimationCurve<float>(duration, [new(Duration.Zero, 0) { Timing = AnimationTimings.CubicBezier(0.42, 0, 0.58, 1), Interpolator = AnimationInterpolators.CubicBezier(2f, 3f, AnimationInterpolators.Float) }, new(duration, 10)], AnimationInterpolators.Float);
        var mapping = new Tween<Duration>(duration, Duration.Zero, duration, AnimationInterpolators.Duration, AnimationEasing.Linear);
        var remap = new AnimationTimeRemap<float>(scalar, mapping);
        var blend = new AnimationBlend<float>(scalar, remap, Scalar(0.5f, 0.5f, duration), AnimationInterpolators.Float);
        var vector = new AnimationHermiteCurve<Vector3>(duration, [new(Duration.Zero, Vector3.Zero, Vector3.Zero, Vector3.One), new(duration, Vector3.One, Vector3.Zero, Vector3.Zero)], AnimationHermiteInterpolators.Vector3);
        var rotation = new AnimationHermiteCurve<Quaternion>(duration, [new(Duration.Zero, Quaternion.Identity, default, default), new(duration, new Quaternion(0, 0, 1, 0), default, default)], AnimationHermiteInterpolators.Quaternion);
        var time = Duration.FromSeconds(0.5);
        double checksum = 0;
        for (int index = 0; index < 2000; index++)
        {
            checksum += blend.Sample(time) + vector.Sample(time).X + rotation.Sample(time).Z;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            checksum += blend.Sample(time) + vector.Sample(time).X + rotation.Sample(time).Z;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(double.IsFinite(checksum) && checksum > 0);
    }

    private static Duration Ticks(long value) => Duration.FromTicks(value);

    private static Tween<float> Scalar(float from, float to, Duration? duration = null) => new(from, to, duration ?? Ticks(10), AnimationInterpolators.Float, AnimationEasing.Linear);

    private sealed class ThrowingTiming : IAnimationTiming
    {
        /// <inheritdoc />
        public double Transform(double amount) => throw new InvalidOperationException("Held keys must not evaluate timing.");
    }

    private sealed class NonFiniteTiming : IAnimationTiming
    {
        /// <inheritdoc />
        public double Transform(double amount) => double.NaN;
    }

    private sealed class ThrowingSource<T>(Duration duration) : IAnimationSource<T>
    {
        /// <inheritdoc />
        public Duration Duration { get; } = duration;

        /// <inheritdoc />
        public T Sample(Duration time) => throw new InvalidOperationException("Unused blend source.");
    }
}
