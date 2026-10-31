using Xunit;

namespace Lumyte.Signals.Tests;

/// <summary>Checks context restoration and revision reduction for direct Action evaluation.</summary>
public sealed class DirectEvaluationTests
{
    /// <summary>Checks a failed initial Effect restores an outer passive capture without leaking its own reads.</summary>
    [Fact]
    public void FailedInitialEffectRestoresOuterCapture()
    {
        var outer = Signal.Create(1);
        var inner = Signal.Create(2);
        _ = Signal.CaptureReads(
            () =>
            {
                Assert.Throws<ArithmeticException>(() => Signal.Effect(() =>
                {
                    _ = inner.Value;
                    throw new ArithmeticException();
                }));
                return outer.Value;
            },
            out SignalReadSet reads);
        int runs = 0;
        using SignalEffect effect = Signal.Effect(() =>
        {
            reads.Read();
            runs++;
        });
        inner.Value = 3;
        Assert.Equal(1, runs);
        outer.Value = 4;
        Assert.Equal(2, runs);
    }

    /// <summary>Checks an empty dependency set has stamp zero and mixed dependencies retain the latest stamp.</summary>
    [Fact]
    public void EmptyAndMixedDependencyRevisionsArePreserved()
    {
        using SignalEffect empty = Signal.Effect(() => { });
        Assert.Equal(0UL, empty.Revision);
        var first = Signal.Create(0);
        var second = Signal.Create(0);
        using SignalEffect firstEffect = Signal.Effect(() => _ = first.Value);
        using SignalEffect secondEffect = Signal.Effect(() => _ = second.Value);
        first.Value = 1;
        second.Value = 2;
        using SignalEffect direct = Signal.Effect(() => _ = first.Value + second.Value);
        Computed<int> total = Signal.Computed(() => first.Value + second.Value);
        using SignalEffect derived = Signal.Effect(() => _ = total.Value);
        Assert.Equal(Math.Max(firstEffect.Revision, secondEffect.Revision), direct.Revision);
        Assert.Equal(direct.Revision, derived.Revision);
        first.Value = 3;
        Assert.Equal(firstEffect.Revision, direct.Revision);
        Assert.Equal(firstEffect.Revision, derived.Revision);
    }

    /// <summary>Checks scheduler rejection still stops delivery and reports its failure once.</summary>
    [Fact]
    public void SchedulerRejectionStillStopsAndReportsTheEffect()
    {
        var source = Signal.Create(0);
        var scheduler = new RejectingScheduler();
        int seen = -1;
        int errors = 0;
        Exception? reported = null;
        using SignalEffect effect = Signal.Effect(
            () => seen = source.Value,
            scheduler,
            error =>
            {
                errors++;
                reported = error;
            });
        source.Value = 1;
        Assert.Same(scheduler.Error, effect.Failure);
        Assert.Same(scheduler.Error, reported);
        Assert.Equal(1, errors);
        Assert.False(effect.IsPending);
        source.Value = 2;
        Assert.Equal(1, scheduler.Calls);
        Assert.Equal(0, seen);
    }

    private sealed class RejectingScheduler : ISignalScheduler
    {
        internal Exception Error { get; } = new ArithmeticException("scheduler rejection");

        internal int Calls { get; private set; }

        public void Schedule(Action work)
        {
            Calls++;
            throw Error;
        }
    }
}
