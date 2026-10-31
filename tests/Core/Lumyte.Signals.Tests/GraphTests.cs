using Xunit;

namespace Lumyte.Signals.Tests;

/// <summary>Checks independent graph evaluation and observer lifetime without a host or pump.</summary>
public sealed class GraphTests
{
    /// <summary>Checks automatic delivery, batch coalescing and fresh diamond values.</summary>
    [Fact]
    public void DiamondBatchAndUntrackedReadsRemainConsistent()
    {
        var source = Signal.Create(1);
        Computed<int> first = Signal.Computed(() => source.Value * 2);
        Computed<int> second = Signal.Computed(() => source.Value + 1);
        Computed<int> sum = Signal.Computed(() => first.Value + second.Value);
        var values = new List<int>();
        using SignalEffect effect = Signal.Effect(() => values.Add(sum.Value));
        Signal.Batch(() =>
        {
            source.Value = 2;
            source.Value = 3;
        });
        Assert.Equal([4, 10], values);
        source.Value = 3;
        Assert.Equal(2, values.Count);
        int calls = 0;
        using SignalEffect untracked = Signal.Effect(() =>
        {
            _ = source.Peek();
            _ = Signal.Untracked(() => sum.Value);
            calls++;
        });
        source.Value = 4;
        Assert.Equal(1, calls);
        Assert.Equal(13, values.Last());
    }

    /// <summary>Checks branch dependencies detach and lazy computed values refresh while unobserved.</summary>
    [Fact]
    public void DynamicAndUnobservedDependenciesAreFresh()
    {
        var choose = Signal.Create(true);
        var left = Signal.Create(1);
        var right = Signal.Create(10);
        Computed<int> selected = Signal.Computed(() => choose.Value ? left.Value : right.Value);
        int runs = 0;
        using SignalEffect effect = Signal.Effect(() =>
        {
            _ = selected.Value;
            runs++;
        });
        right.Value = 11;
        Assert.Equal(1, runs);
        choose.Value = false;
        left.Value = 2;
        Assert.Equal(2, runs);
        effect.Dispose();
        right.Value = 12;
        Assert.Equal(12, selected.Peek());
        right.Value = 13;
        Assert.Equal(13, selected.Value);
    }

    /// <summary>Checks scheduler reservations, paused revisions and disposal invalidate delayed work.</summary>
    [Fact]
    public void ScheduledAndPausedObserversTrackLatestRevision()
    {
        var scheduler = new ManualScheduler();
        var value = Signal.Create(0);
        int seen = -1;
        using SignalEffect effect = Signal.Effect(() => seen = value.Value, scheduler);
        value.Value = 1;
        ulong first = effect.Revision;
        value.Value = 2;
        Assert.Single(scheduler.Work);
        Assert.True(effect.Revision > first);
        effect.Pause();
        value.Value = 3;
        ulong paused = effect.Revision;
        scheduler.Run();
        Assert.Equal(0, seen);
        effect.Resume();
        scheduler.Run();
        Assert.Equal(3, seen);
        Assert.True(effect.Revision >= paused);
        value.Value = 4;
        effect.Dispose();
        scheduler.Run();
        Assert.Equal(3, seen);
        Assert.False(effect.IsPending);
    }

    /// <summary>Checks first failures escape, later failures stop only the observer and batch reports both failures.</summary>
    [Fact]
    public void FailuresAreObservedAndDoNotBreakOtherEffects()
    {
        Assert.Throws<ArithmeticException>(() => Signal.Effect(() => throw new ArithmeticException()));
        var value = Signal.Create(0);
        using SignalEffect broken = Signal.Effect(() =>
        {
            if (value.Value > 0)
            {
                throw new ArithmeticException();
            }
        });
        int seen = 0;
        using SignalEffect healthy = Signal.Effect(() => seen = value.Value);
        Assert.Throws<ArithmeticException>(() => value.Value = 1);
        Assert.IsType<ArithmeticException>(broken.Failure);
        Assert.Equal(1, seen);
        broken.Resume();
        value.Value = 2;
        Assert.Equal(2, seen);
        using SignalEffect second = Signal.Effect(() =>
        {
            if (value.Value > 2)
            {
                throw new FormatException();
            }
        });
        AggregateException error = Assert.Throws<AggregateException>(() => Signal.Batch(() =>
        {
            value.Value = 3;
            throw new InvalidOperationException();
        }));
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Equal(3, seen);
    }

    /// <summary>Checks nested scopes capture effects and disposal does not destroy shared values.</summary>
    [Fact]
    public void ScopesOwnObserversAndKeepFailedSetupEffects()
    {
        var value = Signal.Create(0);
        var outer = new SignalScope();
        using var inner = new SignalScope();
        int first = 0;
        int second = 0;
        Assert.Throws<ArithmeticException>(() => outer.Run(() =>
        {
            _ = Signal.Effect(() => first = value.Value);
            inner.Run(() => _ = Signal.Effect(() => second = value.Value));
            throw new ArithmeticException();
        }));
        outer.Dispose();
        value.Value = 1;
        Assert.Equal(0, first);
        Assert.Equal(1, second);
        inner.Dispose();
        value.Value = 2;
        Assert.Equal(1, second);
        Assert.Equal(2, value.Value);
        Assert.Throws<ObjectDisposedException>(() => outer.Run(() => { }));
    }

    /// <summary>Checks mutation and observer lifecycle reentry from pure evaluation are rejected.</summary>
    [Fact]
    public void CyclesAndWritesDuringEvaluationAreRejected()
    {
        var value = Signal.Create(0);
        Computed<int>? cycle = null;
        cycle = Signal.Computed(() => cycle!.Value);
        Assert.Throws<InvalidOperationException>(() => _ = cycle.Value);
        Assert.Throws<InvalidOperationException>(() => Signal.Effect(() => value.Value++));
        Assert.Throws<InvalidOperationException>(() => Signal.Effect(() => Signal.Batch(() => { })));
        Assert.Throws<InvalidOperationException>(() => Signal.Effect(() => Signal.Effect(() => { })));
        Assert.Equal(0, value.Value);
    }

    /// <summary>Checks passive construction reads can become subscriptions without rerunning construction.</summary>
    [Fact]
    public void PassiveReadSetsSeparateConstructionAndObservation()
    {
        var source = Signal.Create(2);
        int builds = 0;
        int result = Signal.CaptureReads(
            () =>
            {
                builds++;
                return source.Value * 2;
            },
            out SignalReadSet reads);
        Assert.Equal(4, result);
        Assert.True(reads.IsCurrent);
        int runs = 0;
        using SignalEffect effect = Signal.Effect(() =>
        {
            reads.Read();
            runs++;
        });
        source.Value = 3;
        Assert.False(reads.IsCurrent);
        Assert.Equal(2, runs);
        Assert.Equal(1, builds);
    }

    /// <summary>Checks sequential use can move threads without a creator-thread restriction.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ValuesCanMoveBetweenThreadsWhenOperationsAreSequentialAsync()
    {
        var value = Signal.Create(1);
        Computed<int> doubled = Signal.Computed(() => value.Value * 2);
        Assert.Equal(2, doubled.Value);
        await Task.Run(() => value.Value = 4);
        Assert.Equal(8, doubled.Value);
    }

    /// <summary>Checks a caught computed failure remains subscribed, including a failure on the first read.</summary>
    /// <param name="initialFailure">Whether the first computed read fails.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaughtComputedFailuresKeepTheirDependencies(bool initialFailure)
    {
        var divisor = Signal.Create(initialFailure ? 0 : 1);
        Computed<int> quotient = Signal.Computed(() => 10 / divisor.Value);
        var results = new List<string>();
        using SignalEffect effect = Signal.Effect(() =>
        {
            try
            {
                results.Add(quotient.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (DivideByZeroException)
            {
                results.Add("error");
            }
        });
        if (!initialFailure)
        {
            divisor.Value = 0;
        }

        divisor.Value = 2;
        Assert.Equal(initialFailure ? ["error", "5"] : new[] { "10", "error", "5" }, results);
        Assert.Null(effect.Failure);
    }

    /// <summary>Checks an unobserved computed stops checking obsolete branches as soon as a dependency changes.</summary>
    /// <param name="peek">Whether the read is untracked.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnobservedComputedSkipsObsoleteFailingBranches(bool peek)
    {
        var chooseLeft = Signal.Create(true);
        var divisor = Signal.Create(1);
        Computed<int> left = Signal.Computed(() => 10 / divisor.Value);
        Computed<int> selected = Signal.Computed(() => chooseLeft.Value ? left.Value : 42);
        Assert.Equal(10, selected.Value);
        chooseLeft.Value = false;
        divisor.Value = 0;
        Assert.Equal(42, peek ? selected.Peek() : selected.Value);
        Assert.Equal(42, selected.Value);
    }

    private sealed class ManualScheduler : ISignalScheduler
    {
        internal Queue<Action> Work { get; } = [];

        public void Schedule(Action work) => Work.Enqueue(work);

        internal void Run()
        {
            while (Work.TryDequeue(out Action? work))
            {
                work();
            }
        }
    }
}
