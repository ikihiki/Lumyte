using System.Collections;
using System.Numerics;
using Lumyte.Core.Time;
using Xunit;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Benchmark.Tests;

/// <summary>Analytic values and observable sharing contracts for executable curve/source workloads.</summary>
public sealed class CurveAndSourceTests
{
    /// <summary>Checks every bounded curve profile against an independent segment formula.</summary>
    [Fact]
    public void CurveWavesHaveAnalyticValues()
    {
        foreach (int count in new[] { 8, 128 })
        {
            foreach (string segment in new[] { "Linear", "Hold", "TemporalBezier", "SpatialBezier" })
            {
                foreach (string profile in new[] { "Interior", "Boundaries" })
                {
                    var fixture = new CurveBenchmarks { KeyCount = count, Segment = segment, InputProfile = profile };
                    fixture.Setup();
                    AnimationCurve<float> curve = fixture.ConstructCurve();
                    AnimationHermiteCurve<float> hermite = fixture.ConstructHermite();
                    Assert.Equal(0, curve.Sample(Duration.Zero));
                    Assert.Equal(count - 1, curve.Sample(Duration.FromSeconds(count + 1)));
                    Assert.Equal(2, curve.Sample(Duration.FromSeconds(3)));
                    Assert.Equal(2.5f, hermite.Sample(Duration.FromSeconds(3.5)), 5);
                    float expected = segment == "Hold" ? 2 : 2.5f;
                    Assert.Equal(expected, curve.Sample(Duration.FromSeconds(3.5)), 4);
                    float curveSum = 0;
                    float hermiteSum = 0;
                    for (int index = 0; index < 64; index++)
                    {
                        int key = index * count / 64;
                        double seconds = profile == "Interior"
                            ? key + 1 + (3_333_333 / 10_000_000.0)
                            : (index % 4) switch
                            {
                                0 => 0,
                                1 => count + 1,
                                2 => key + 1,
                                _ => key + 1 - 0.0000001,
                            };
                        double linear = Math.Clamp(seconds - 1, 0, count - 1);
                        double whole = Math.Floor(linear);
                        double amount = linear - whole;
                        double transformed = segment switch
                        {
                            "Hold" => 0,
                            "SpatialBezier" => (0.75 * amount * (1 - amount) * (1 - amount)) + (2.25 * amount * amount * (1 - amount)) + (amount * amount * amount),
                            "TemporalBezier" => AnimationTimings.CubicBezier(0.42, 0, 0.58, 1).Transform(amount),
                            _ => amount,
                        };
                        curveSum += (float)(whole + transformed);
                        hermiteSum += (float)linear;
                    }

                    Assert.InRange(MathF.Abs(curveSum - fixture.SampleCurve()), 0, 0.002f);
                    Assert.InRange(MathF.Abs(hermiteSum - fixture.SampleHermite()), 0, 0.002f);
                    Assert.Equal(fixture.SampleCurve(), fixture.SampleCurve());
                }
            }
        }
    }

    /// <summary>Checks inverse-x timing, overshoot, zero duration, and non-linear per-second tangents.</summary>
    [Fact]
    public void TimingAndHermiteHaveIndependentAnalyticOracles()
    {
        IAnimationTiming timing = AnimationTimings.CubicBezier(0, 1.0 / 3, 1, 2.0 / 3);
        Assert.Equal(0.25, timing.Transform(0.15625), 6);
        IAnimationTiming overshoot = AnimationTimings.CubicBezier(0, 2, 1, 2);
        Assert.Equal(1.625, overshoot.Transform(0.5), 6);
        var duration = Duration.FromSeconds(2);
        var hermite = new AnimationHermiteCurve<float>(duration, [new(Duration.Zero, 0, 0, 0), new(duration, 8, 0, 0)], AnimationHermiteInterpolators.Float);
        Assert.Equal(1.25f, hermite.Sample(Duration.FromSeconds(0.5)), 5);
        var tangent = new AnimationHermiteCurve<float>(duration, [new(Duration.Zero, 0, 0, 4), new(duration, 8, 4, 0)], AnimationHermiteInterpolators.Float);
        Assert.Equal(2, tangent.Sample(Duration.FromSeconds(0.5)), 5);
        var instant = new Tween<float>(2, 7, Duration.Zero, AnimationInterpolators.Float, AnimationEasing.Linear);
        Assert.Equal(7, instant.Sample(Duration.Zero));
    }

    /// <summary>Checks polynomial easing, typed time, vector and rotation consumption.</summary>
    /// <param name="easing">The polynomial rule.</param>
    /// <param name="quarter">The analytic quarter-time progress.</param>
    [Theory]
    [InlineData(AnimationEasing.Linear, 0.25f)]
    [InlineData(AnimationEasing.EaseIn, 0.0625f)]
    [InlineData(AnimationEasing.EaseOut, 0.4375f)]
    [InlineData(AnimationEasing.EaseInOut, 0.125f)]
    public void TweenProfilesMatchAnalyticProgress(AnimationEasing easing, float quarter)
    {
        var fixture = new TweenBenchmarks { Easing = easing };
        fixture.Setup();
        Assert.Equal(quarter, fixture.ConstructTween().Sample(Duration.FromSeconds(0.25)));
        double sum = 0;
        double rotationSum = 0;
        for (int index = 0; index <= 64; index++)
        {
            double t = index / 64.0;
            double amount = easing switch
            {
                AnimationEasing.EaseIn => t * t,
                AnimationEasing.EaseOut => 1 - ((1 - t) * (1 - t)),
                AnimationEasing.EaseInOut => t < 0.5 ? 2 * t * t : 1 - (2 * (1 - t) * (1 - t)),
                _ => t,
            };
            sum += amount;
            rotationSum += Math.Sin(amount * Math.PI / 4) + Math.Cos(amount * Math.PI / 4);
        }

        Assert.Equal((float)sum, fixture.SampleScalar(), 4);
        Assert.InRange(Vector3.Distance(new Vector3(2, 4, 6) * (float)sum, fixture.SampleVector()), 0, 0.0001f);
        Assert.InRange(Math.Abs(fixture.SampleDuration() - (sum * TimeSpan.TicksPerSecond)), 0, 65);
        Assert.Equal((float)rotationSum, fixture.SampleRotation(), 4);
        Assert.Equal(32.5, fixture.TransformTiming(), 5);
    }

    /// <summary>Checks direct and compiled graphs at every depth/time profile, including repeated workspace use.</summary>
    [Fact]
    public void SharedGraphWavesPreserveExpectedValues()
    {
        foreach (int depth in new[] { 2, 8 })
        {
            foreach (string profile in new[] { "SameLocalTime", "DifferentLocalTime" })
            {
                var fixture = new SourceGraphBenchmarks { Depth = depth, InputProfile = profile };
                fixture.Setup();
                IAnimationSource<float> source = fixture.Compile();
                Assert.Equal(profile == "SameLocalTime" ? 25 : 50, source.Sample(Duration.FromSeconds(0.25)), 4);
                Assert.Equal(profile == "SameLocalTime" ? 75 : 50, source.Sample(Duration.FromSeconds(0.75)), 4);
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    Assert.Equal(1600, fixture.SampleCompiled(), 2);
                    Assert.Equal(fixture.SampleDirect(), fixture.SampleCompiled(), 3);
                }
            }
        }
    }

    /// <summary>Checks per-call cache isolation and same-versus-different local-time sampling counts.</summary>
    /// <param name="reverse">Whether the second child reverses time.</param>
    /// <param name="calls">The expected leaf calls per root evaluation.</param>
    /// <param name="expected">The analytic blended value.</param>
    [Theory]
    [InlineData(false, 1, 25)]
    [InlineData(true, 2, 50)]
    public void SharedLeafCountsDistinguishLocalTimes(bool reverse, int calls, float expected)
    {
        var leaf = new CountingSource();
        Definitions.Sampled<float> value = Sampled<float>(leaf);
        Definitions.Sampled<Duration> forward = Sampled<Duration>(new Tween<Duration>(Duration.Zero, Duration.FromSeconds(1), Duration.FromSeconds(1), AnimationInterpolators.Duration, AnimationEasing.Linear));
        Definitions.Sampled<Duration> backward = Sampled<Duration>(new Tween<Duration>(Duration.FromSeconds(1), Duration.Zero, Duration.FromSeconds(1), AnimationInterpolators.Duration, AnimationEasing.Linear));
        Definitions.Sampled<float> weight = Sampled<float>(new Tween<float>(0.5f, 0.5f, Duration.FromSeconds(1), AnimationInterpolators.Float, AnimationEasing.Linear));
        IAnimationSource<float> source = Blend<float>(from: TimeRemap<float>(value: value, timeMap: forward), to: TimeRemap<float>(value: value, timeMap: reverse ? backward : forward), weight: weight, interpolator: AnimationInterpolators.Float).Build();
        Assert.Equal(expected, source.Sample(Duration.FromSeconds(0.25)), 4);
        Assert.Equal(calls, leaf.Calls);
        Assert.Equal(expected, source.Sample(Duration.FromSeconds(0.25)), 4);
        Assert.Equal(calls * 2, leaf.Calls);
        Assert.Equal(reverse ? 50 : 75, source.Sample(Duration.FromSeconds(0.75)), 4);
        Assert.Equal(calls * 3, leaf.Calls);
    }

    /// <summary>Checks one copy per shared definition and independent snapshots after a valid definition mutation.</summary>
    [Fact]
    public void CompilationSnapshotsSharedKeysOnceAndRebuildsAfterMutation()
    {
        AnimationKey<float>[] keys = [new(Duration.Zero, 0), new(Duration.FromSeconds(1), 100)];
        var counted = new CountingKeys(keys);
        Definitions.Curve<float> curve = Curve<float>(Duration.FromSeconds(1), AnimationInterpolators.Float);
        curve.Keys = counted;
        Definitions.Sampled<float> weight = Sampled<float>(new Tween<float>(0.5f, 0.5f, Duration.FromSeconds(1), AnimationInterpolators.Float, AnimationEasing.Linear));
        Definitions.Blend<float> definition = Blend<float>(from: curve, to: curve, weight: weight, interpolator: AnimationInterpolators.Float);
        IAnimationSource<float> original = definition.Build();
        Assert.Equal(1, counted.Enumerations);
        IAnimationSource<float> unchanged = definition.Build();
        Assert.Equal(2, counted.Enumerations);
        Assert.Equal(25, unchanged.Sample(Duration.FromSeconds(0.25)));
        keys[1] = new(Duration.FromSeconds(1), 200);
        IAnimationSource<float> changed = definition.Build();
        Assert.Equal(3, counted.Enumerations);
        Assert.Equal(25, original.Sample(Duration.FromSeconds(0.25)));
        Assert.Equal(25, unchanged.Sample(Duration.FromSeconds(0.25)));
        Assert.Equal(50, changed.Sample(Duration.FromSeconds(0.25)));
    }

    private sealed class CountingSource : IAnimationSource<float>
    {
        /// <summary>Gets the observation-only sample count.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public Duration Duration => Duration.FromSeconds(1);

        /// <inheritdoc />
        public float Sample(Duration time)
        {
            Calls++;
            return (float)(time.TotalSeconds * 100);
        }
    }

    private sealed class CountingKeys(AnimationKey<float>[] values) : IReadOnlyList<AnimationKey<float>>
    {
        /// <summary>Gets the source snapshot count.</summary>
        public int Enumerations { get; private set; }

        /// <inheritdoc />
        public int Count => values.Length;

        /// <inheritdoc />
        public AnimationKey<float> this[int index] => values[index];

        /// <inheritdoc />
        public IEnumerator<AnimationKey<float>> GetEnumerator()
        {
            Enumerations++;
            return ((IEnumerable<AnimationKey<float>>)values).GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
