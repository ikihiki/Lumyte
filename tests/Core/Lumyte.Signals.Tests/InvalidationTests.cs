using Xunit;

namespace Lumyte.Signals.Tests;

/// <summary>Exercises invalidation paths after replacing traversal allocations with mutation stamps.</summary>
public sealed class InvalidationTests
{
    /// <summary>Checks diamond paths deliver one consistent result per source write.</summary>
    [Fact]
    public void SharedPathsDeliverOncePerWrite()
    {
        var source = Signal.Create(0);
        Computed<int> left = Signal.Computed(() => source.Value + 1);
        Computed<int> right = Signal.Computed(() => source.Value * 2);
        Computed<int> sum = Signal.Computed(() => left.Value + right.Value);
        var seen = new List<int>();
        using SignalEffect effect = Signal.Effect(() => seen.Add(sum.Value + source.Value));
        for (int value = 1; value <= 20; value++)
        {
            source.Value = value;
            Assert.Equal((4 * value) + 1, seen[^1]);
        }

        Assert.Equal(21, seen.Count);
    }

    /// <summary>Checks queued and paused observers still receive every newer upstream revision.</summary>
    [Fact]
    public void PausedDiamondRetainsLatestRevision()
    {
        var scheduler = new Scheduler();
        var source = Signal.Create(0);
        Computed<int> left = Signal.Computed(() => source.Value + 1);
        Computed<int> right = Signal.Computed(() => source.Value * 2);
        int seen = -1;
        using SignalEffect effect = Signal.Effect(() => seen = left.Value + right.Value, scheduler);
        source.Value = 1;
        ulong previous = effect.Revision;
        effect.Pause();
        for (int value = 2; value <= 20; value++)
        {
            source.Value = value;
            Assert.True(effect.Revision > previous);
            previous = effect.Revision;
        }

        scheduler.Drain();
        Assert.Equal(1, seen);
        effect.Resume();
        scheduler.Drain();
        Assert.Equal(61, seen);
        Assert.False(effect.IsPending);
    }

    /// <summary>Checks dynamic subscriptions change safely after each traversal has completed.</summary>
    [Fact]
    public void DynamicBranchesChangeSubscriptionsAfterTraversal()
    {
        var choose = Signal.Create(true);
        var left = Signal.Create(0);
        var right = Signal.Create(100);
        Computed<int> selected = Signal.Computed(() => choose.Value ? left.Value : right.Value);
        int seen = -1;
        int notifications = 0;
        using SignalEffect effect = Signal.Effect(() =>
        {
            seen = selected.Value;
            notifications++;
        });
        for (int index = 1; index <= 20; index++)
        {
            choose.Value = false;
            Assert.Equal(right.Value, seen);
            int before = notifications;
            left.Value = index;
            Assert.Equal(before, notifications);
            choose.Value = true;
            Assert.Equal(index, seen);
            before = notifications;
            right.Value = index + 100;
            Assert.Equal(before, notifications);
        }
    }

    /// <summary>Checks a caught recursive read cannot cause an endless invalidation cycle.</summary>
    [Fact]
    public void CaughtReadCycleDoesNotLoopDuringInvalidation()
    {
        var source = Signal.Create(0);
        Computed<int>? first = null;
        Computed<int>? second = null;
        first = Signal.Computed(() => source.Value + second!.Value);
        second = Signal.Computed(() =>
        {
            try
            {
                _ = first.Value;
            }
            catch (InvalidOperationException)
            {
            }

            return source.Value;
        });
        int seen = -1;
        using SignalEffect effect = Signal.Effect(() => seen = first.Value);
        for (int value = 1; value <= 10; value++)
        {
            source.Value = value;
            Assert.Equal(value * 2, seen);
            Assert.Null(effect.Failure);
        }
    }

    /// <summary>Checks removing queued subscriptions does not disturb remaining subscribers.</summary>
    [Fact]
    public void DisposedQueuedObserverLeavesOtherObserversActive()
    {
        var source = Signal.Create(0);
        var scheduler = new Scheduler();
        int late = -1;
        int live = -1;
        SignalEffect disposed = Signal.Effect(() => late = source.Value, scheduler);
        using SignalEffect survivor = Signal.Effect(() => live = source.Value);
        source.Value = 1;
        disposed.Dispose();
        source.Value = 2;
        scheduler.Drain();
        Assert.Equal(0, late);
        Assert.Equal(2, live);
    }

    /// <summary>Checks mutation stamps remain distinct when writers move between threads.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task DifferentThreadsKeepDistinctStampsAsync()
    {
        var source = Signal.Create(0);
        var seen = new List<int>();
        using SignalEffect effect = Signal.Effect(() => seen.Add(source.Value));
        await Task.Run(() => source.Value = 1);
        ulong first = effect.Revision;
        await Task.Run(() => source.Value = 2);
        Assert.True(effect.Revision > first);
        Assert.Equal([0, 1, 2], seen);
    }

    private sealed class Scheduler : ISignalScheduler
    {
        private readonly Queue<Action> _work = new();

        public void Schedule(Action work) => _work.Enqueue(work);

        internal void Drain()
        {
            while (_work.TryDequeue(out Action? work))
            {
                work();
            }
        }
    }
}
