using System.Collections;
using Lumyte.Core.Time;
using Xunit;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Tests;

/// <summary>Checks source graph snapshots preserve sharing and reject cycles.</summary>
public sealed class SourceCompilationTests
{
    /// <summary>Checks a small shared graph does not repeatedly copy its leaf keys.</summary>
    [Fact]
    public void FibonacciGraphCopiesEachSharedCurveOnlyOnce()
    {
        var firstKeys = new CountingList<AnimationKey<float>>([new(Duration.Zero, 0), new(Ticks(10), 10)]);
        var secondKeys = new CountingList<AnimationKey<float>>([new(Duration.Zero, 10), new(Ticks(10), 20)]);
        var weightKeys = new CountingList<AnimationKey<float>>([new(Duration.Zero, 0.5f)]);
        Definitions.Curve<float> first = Curve<float>(Ticks(10), AnimationInterpolators.Float);
        first.Keys = firstKeys;
        Definitions.Curve<float> second = Curve<float>(Ticks(10), AnimationInterpolators.Float);
        second.Keys = secondKeys;
        Definitions.Source<float> previous = first;
        Definitions.Source<float> current = second;
        Definitions.Curve<float> weight = Curve<float>(Ticks(10), AnimationInterpolators.Float);
        weight.Keys = weightKeys;
        for (int index = 0; index < 24; index++)
        {
            Definitions.Blend<float> next = Blend<float>(from: previous, to: current, weight: weight, interpolator: AnimationInterpolators.Float);
            previous = current;
            current = next;
        }

        IAnimationSource<float> compiled = current.Build();
        Assert.Equal(Ticks(10), compiled.Duration);
        Assert.Equal(1, firstKeys.EnumerationCount);
        Assert.Equal(1, secondKeys.EnumerationCount);
        Assert.Equal(1, weightKeys.EnumerationCount);
    }

    /// <summary>Checks shared graphs copy mixed value types once per independent Build snapshot.</summary>
    [Fact]
    public void IndependentBuildsSnapshotSharedHeterogeneousSourcesAgain()
    {
        AnimationKey<float>[] values = [new(Duration.Zero, 0), new(Ticks(10), 10)];
        var valueKeys = new CountingList<AnimationKey<float>>(values);
        var timeKeys = new CountingList<AnimationKey<Duration>>([new(Duration.Zero, Ticks(10)), new(Ticks(10), Duration.Zero)]);
        Definitions.Curve<float> value = Curve<float>(Ticks(10), AnimationInterpolators.Float);
        value.Keys = valueKeys;
        Definitions.Curve<Duration> time = Curve<Duration>(Ticks(10), AnimationInterpolators.Duration);
        time.Keys = timeKeys;
        Definitions.TimeRemap<float> remap = TimeRemap<float>(value: value, timeMap: time);
        Definitions.Sampled<float> weight = Sampled<float>(new Tween<float>(0.5f, 0.5f, Ticks(10), AnimationInterpolators.Float, AnimationEasing.Linear));
        Definitions.Blend<float> definition = Blend<float>(from: remap, to: remap, weight: weight, interpolator: AnimationInterpolators.Float);
        IAnimationSource<float> first = definition.Build();
        Assert.Equal(1, valueKeys.EnumerationCount);
        Assert.Equal(1, timeKeys.EnumerationCount);
        values[0] = new(Duration.Zero, 100);
        values[1] = new(Ticks(10), 100);
        IAnimationSource<float> second = definition.Build();
        Assert.NotSame(first, second);
        Assert.Equal(5, first.Sample(Ticks(5)));
        Assert.Equal(100, second.Sample(Ticks(5)));
        Assert.Equal(2, valueKeys.EnumerationCount);
        Assert.Equal(2, timeKeys.EnumerationCount);
    }

    /// <summary>Checks distinct definitions that compare equal still produce distinct values.</summary>
    [Fact]
    public void SharingUsesDefinitionIdentityInsteadOfValueEquality()
    {
        var from = new EqualCurve
        {
            Duration = Ticks(10),
            Interpolator = AnimationInterpolators.Float,
            Keys = [new(Duration.Zero, 0)],
        };
        var to = new EqualCurve
        {
            Duration = Ticks(10),
            Interpolator = AnimationInterpolators.Float,
            Keys = [new(Duration.Zero, 10)],
        };
        Definitions.Sampled<float> weight = Sampled<float>(new Tween<float>(0.5f, 0.5f, Ticks(10), AnimationInterpolators.Float, AnimationEasing.Linear));
        Definitions.Blend<float> definition = Blend<float>(from: from, to: to, weight: weight, interpolator: AnimationInterpolators.Float);
        Assert.Equal(5, definition.Build().Sample(Ticks(5)));
    }

    /// <summary>Checks a cached sibling cannot hide a cycle and a failed Build does not retain its cache.</summary>
    [Fact]
    public void CycleAfterSharedSiblingStillFailsAndCanBeCorrected()
    {
        var keys = new CountingList<AnimationKey<float>>([new(Duration.Zero, 0.5f)]);
        Definitions.Curve<float> shared = Curve<float>(Ticks(10), AnimationInterpolators.Float);
        shared.Keys = keys;
        Definitions.Blend<float> child = Blend<float>(from: shared, to: shared, weight: shared, interpolator: AnimationInterpolators.Float);
        Definitions.Blend<float> root = Blend<float>(from: shared, to: child, weight: shared, interpolator: AnimationInterpolators.Float);
        typeof(Definitions.Blend<float>).GetProperty(nameof(child.To))!.SetValue(child, root);
        Assert.Throws<ArgumentException>(() => root.Build());
        Assert.Equal(1, keys.EnumerationCount);
        typeof(Definitions.Blend<float>).GetProperty(nameof(child.To))!.SetValue(child, shared);
        Assert.Equal(0.5f, root.Build().Sample(Ticks(5)));
        Assert.Equal(2, keys.EnumerationCount);
    }

    private static Duration Ticks(long value) => Duration.FromTicks(value);

    private sealed class CountingList<T>(T[] values) : IReadOnlyList<T>
    {
        /// <summary>Gets the number of key snapshot enumerations.</summary>
        public int EnumerationCount { get; private set; }

        /// <inheritdoc />
        public int Count => values.Length;

        /// <inheritdoc />
        public T this[int index] => values[index];

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return ((IEnumerable<T>)values).GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class EqualCurve : Definitions.Curve<float>
    {
        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is EqualCurve;

        /// <inheritdoc />
        public override int GetHashCode() => 0;
    }
}
