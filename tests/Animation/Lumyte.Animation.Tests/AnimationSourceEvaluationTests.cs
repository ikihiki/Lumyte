using Lumyte.Core.Time;
using Xunit;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Tests;

/// <summary>Checks shared source graphs reuse evaluations within each independent sample.</summary>
public sealed class AnimationSourceEvaluationTests
{
    /// <summary>Checks a shared graph evaluates each node once instead of expanding every path.</summary>
    [Fact]
    public void SharedGraphEvaluatesEachNodeOncePerLocalTime()
    {
        var first = new CountingSource(time => time.Ticks);
        var second = new CountingSource(time => time.Ticks + 1);
        var interpolator = new CountingInterpolator();
        Definitions.Source<float> previous = Sampled<float>(first);
        Definitions.Source<float> current = Sampled<float>(second);
        Definitions.Source<float> weight = Constant(0.5f);
        float expectedPrevious = 5;
        float expectedCurrent = 6;
        for (int i = 0; i < 24; i++)
        {
            Definitions.Source<float> next = Blend(from: previous, to: current, weight: weight, interpolator: interpolator);
            previous = current;
            current = next;
            float nextExpected = AnimationInterpolators.Float.Interpolate(expectedPrevious, expectedCurrent, 0.5f);
            expectedPrevious = expectedCurrent;
            expectedCurrent = nextExpected;
        }

        IAnimationSource<float> graph = current.Build();
        Assert.Equal(expectedCurrent, graph.Sample(Ticks(5)));
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Equal(24, interpolator.Calls);
        Assert.Equal(expectedCurrent, graph.Sample(Ticks(5)));
        Assert.Equal(2, first.Calls);
        Assert.Equal(2, second.Calls);
        Assert.Equal(48, interpolator.Calls);
    }

    /// <summary>Checks shared sources use separate entries for each remapped time in one sample.</summary>
    [Fact]
    public void SharedSourceCachesEachRemappedTimeIndependently()
    {
        var times = new List<long>();
        var source = new CountingSource(time =>
        {
            times.Add(time.Ticks);
            return time.Ticks;
        });
        Definitions.Source<float> shared = Sampled<float>(source);
        Definitions.Source<float> weight = Constant(0.5f);
        Definitions.Source<Duration> start = ConstantTime(Duration.Zero);
        Definitions.Source<Duration> end = ConstantTime(Ticks(10));
        IAnimationSource<float> graph = Blend(
            from: Blend(from: TimeRemap(value: shared, timeMap: start), to: TimeRemap(value: shared, timeMap: end), weight: weight, interpolator: AnimationInterpolators.Float),
            to: Blend(from: TimeRemap(value: shared, timeMap: start), to: TimeRemap(value: shared, timeMap: end), weight: weight, interpolator: AnimationInterpolators.Float),
            weight: weight,
            interpolator: AnimationInterpolators.Float).Build();

        Assert.Equal(5, graph.Sample(Ticks(5)));
        Assert.Equal([0L, 10L], times);
        Assert.Equal(5, graph.Sample(Ticks(6)));
        Assert.Equal([0L, 10L, 0L, 10L], times);
    }

    /// <summary>Checks pooled graph evaluation preserves zero and one weight short circuiting.</summary>
    [Fact]
    public void SharedGraphDoesNotEvaluateUnselectedBranches()
    {
        var source = new CountingSource(time => time.Ticks);
        var throwing = new CountingSource(_ => throw new InvalidOperationException());
        Definitions.Source<float> shared = Sampled<float>(source);
        Definitions.Source<float> unused = Sampled<float>(throwing);
        IAnimationSource<float> graph = Blend(
            from: Blend(from: shared, to: unused, weight: Constant(0), interpolator: AnimationInterpolators.Float),
            to: Blend(from: unused, to: shared, weight: Constant(1), interpolator: AnimationInterpolators.Float),
            weight: Constant(0.5f),
            interpolator: AnimationInterpolators.Float).Build();

        Assert.Equal(5, graph.Sample(Ticks(5)));
        Assert.Equal(1, source.Calls);
        Assert.Equal(0, throwing.Calls);
    }

    /// <summary>Checks an exception discards cached values before the pooled context is reused.</summary>
    [Fact]
    public void FailedSampleDoesNotReusePartialResults()
    {
        bool fail = true;
        var source = new CountingSource(time => time.Ticks);
        var conditional = new CountingSource(time => fail ? throw new InvalidOperationException() : time.Ticks);
        Definitions.Source<float> shared = Sampled<float>(source);
        Definitions.Source<float> weight = Constant(0.5f);
        IAnimationSource<float> graph = Blend(
            from: shared,
            to: Blend(from: shared, to: Sampled<float>(conditional), weight: weight, interpolator: AnimationInterpolators.Float),
            weight: weight,
            interpolator: AnimationInterpolators.Float).Build();

        Assert.Throws<InvalidOperationException>(() => graph.Sample(Ticks(5)));
        fail = false;
        Assert.Equal(5, graph.Sample(Ticks(5)));
        Assert.Equal(2, source.Calls);
        Assert.Equal(2, conditional.Calls);
    }

    /// <summary>Checks reentrant calls lease independent contexts without corrupting the outer sample.</summary>
    [Fact]
    public void ReentrantSamplesUseIndependentContexts()
    {
        IAnimationSource<float>? graph = null;
        var source = new CountingSource(time => time == Duration.Zero ? 0 : graph!.Sample(Ticks(time.Ticks - 1)) + 1);
        Definitions.Source<float> shared = Sampled<float>(source);
        graph = Blend(from: shared, to: shared, weight: Constant(0.5f), interpolator: AnimationInterpolators.Float).Build();

        Assert.Equal(5, graph.Sample(Ticks(5)));
        Assert.Equal(6, source.Calls);
        Assert.Equal(3, graph.Sample(Ticks(3)));
        Assert.Equal(10, source.Calls);
    }

    /// <summary>Checks concurrent calls cannot observe cached values or dictionary mutations from one another.</summary>
    [Fact]
    public void ParallelSamplesUseIndependentContexts()
    {
        var source = new CountingSource(time =>
        {
            Thread.SpinWait(100);
            return time.Ticks;
        });
        Definitions.Source<float> shared = Sampled<float>(source);
        IAnimationSource<float> graph = Blend(from: shared, to: shared, weight: Constant(0.5f), interpolator: AnimationInterpolators.Float).Build();

        System.Threading.Tasks.Parallel.For(0, 2048, i => Assert.Equal(i % 11, graph.Sample(Ticks(i % 11))));
        Assert.Equal(2048, source.Calls);
    }

    /// <summary>Checks warmed shared graph sampling retains reusable typed storage without allocation.</summary>
    [Fact]
    public void WarmedSharedGraphSamplingDoesNotAllocate()
    {
        Definitions.Source<float> shared = Curve<float>(Ticks(10), AnimationInterpolators.Float)[new AnimationKey<float>(Duration.Zero, 0), new AnimationKey<float>(Ticks(10), 10)];
        Definitions.Source<float> weight = Constant(0.5f);
        Definitions.Source<float> current = shared;
        for (int i = 0; i < 24; i++)
        {
            current = Blend(from: current, to: current, weight: weight, interpolator: AnimationInterpolators.Float);
        }

        IAnimationSource<float> graph = current.Build();
        for (int i = 0; i < 1000; i++)
        {
            _ = graph.Sample(Ticks(i % 11));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            sum += graph.Sample(Ticks(i % 11));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sum > 0);
        Assert.Equal(0, allocated);
    }

    private static Definitions.Source<float> Constant(float value) => Curve<float>(Ticks(10), AnimationInterpolators.Float)[new AnimationKey<float>(Duration.Zero, value)];

    private static Definitions.Source<Duration> ConstantTime(Duration value) => Curve<Duration>(Ticks(10), AnimationInterpolators.Duration)[new AnimationKey<Duration>(Duration.Zero, value)];

    private static Duration Ticks(long ticks) => Duration.FromTicks(ticks);

    private sealed class CountingSource(Func<Duration, float> sample) : IAnimationSource<float>
    {
        private int _calls;

        /// <inheritdoc />
        public Duration Duration => Ticks(10);

        /// <summary>Gets the number of evaluations.</summary>
        public int Calls => _calls;

        /// <inheritdoc />
        public float Sample(Duration time)
        {
            Interlocked.Increment(ref _calls);
            return sample(time);
        }
    }

    private sealed class CountingInterpolator : IAnimationInterpolator<float>
    {
        /// <summary>Gets the number of interpolations.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public float Interpolate(float from, float to, float amount)
        {
            Calls++;
            return AnimationInterpolators.Float.Interpolate(from, to, amount);
        }
    }
}
