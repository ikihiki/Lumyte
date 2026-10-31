using System.Collections.Concurrent;
using Xunit;

namespace Lumyte.Signals.Tests;

/// <summary>Checks asynchronous conversion, latest-operation publication and owned cancellation.</summary>
public sealed class AsyncTests
{
    /// <summary>Checks pending and successful snapshots participate in the normal graph.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CompletionPublishesOneConsistentSnapshotAsync()
    {
        var pending = new TaskCompletionSource<int>();
        using AsyncSignal<int> signal = Signal.FromAsync(_ => pending.Task);
        var statuses = new ConcurrentQueue<AsyncSignalStatus>();
        Computed<bool> loading = Signal.Computed(() => signal.Value.IsLoading);
        using SignalEffect effect = Signal.Effect(() => statuses.Enqueue(signal.Value.Status));
        Assert.True(loading.Value);
        Assert.False(signal.Peek().HasValue);
        pending.SetResult(42);
        AsyncSignalState<int> state = await signal.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(42, state.Result);
        Assert.True(state.HasValue);
        Assert.False(loading.Value);
        Assert.Same(state, signal.Value);
        Assert.Equal([AsyncSignalStatus.Pending, AsyncSignalStatus.Succeeded], statuses.ToArray());
    }

    /// <summary>Checks null and default are successful values rather than missing data.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task NullAndDefaultRemainSuccessfulAsync()
    {
        using AsyncSignal<string?> nullable = Signal.FromAsync(async () =>
        {
            await Task.Yield();
            return (string?)null;
        });
        Assert.True((await nullable.Completion).HasValue);
        Assert.Null(nullable.Value.Result);
        using AsyncSignal<int> zero = Signal.FromAsync(() => Task.FromResult(0));
        Assert.True(zero.Value.HasValue);
        Assert.Equal(0, zero.Value.Result);
    }

    /// <summary>Checks synchronous and asynchronous loader failures become data.</summary>
    /// <param name="synchronous">Whether invoking the loader itself throws.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoaderFailuresAreObservableStateAsync(bool synchronous)
    {
        var failure = new ArithmeticException("loader");
        using AsyncSignal<int> signal = Signal.FromAsync<int>(() => synchronous ? throw failure : Task.FromException<int>(failure));
        AsyncSignalState<int> state = await signal.Completion;
        Assert.Equal(AsyncSignalStatus.Failed, state.Status);
        Assert.Same(failure, state.Error);
        Assert.False(state.HasValue);
    }

    /// <summary>Checks cancellation raised by a loader does not become failure.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task LoaderCancellationIsDistinctFromFailureAsync()
    {
        using AsyncSignal<int> signal = Signal.FromAsync(() => Task.FromCanceled<int>(new CancellationToken(true)));
        Assert.Equal(AsyncSignalStatus.Canceled, (await signal.Completion).Status);
        Assert.Null(signal.Value.Error);
    }

    /// <summary>Checks obsolete work cannot replace the newest result even when it ignores cancellation.</summary>
    /// <param name="failObsolete">Whether the obsolete work fails instead of succeeding.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReloadRevokesOldCompletionAsync(bool failObsolete)
    {
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        int calls = 0;
        CancellationToken firstToken = default;
        using AsyncSignal<int> signal = Signal.FromAsync(token =>
        {
            calls++;
            if (calls == 1)
            {
                firstToken = token;
                return first.Task;
            }

            return second.Task;
        });
        Task<AsyncSignalState<int>> obsolete = signal.Completion;
        Task<AsyncSignalState<int>> latest = signal.ReloadAsync();
        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(AsyncSignalStatus.Canceled, (await obsolete).Status);
        second.SetResult(20);
        Assert.Equal(20, (await latest).Result);
        if (failObsolete)
        {
            first.SetException(new ArithmeticException("obsolete"));
        }
        else
        {
            first.SetResult(10);
        }

        Assert.Equal(20, signal.Value.Result);
        Assert.Same(latest, signal.Completion);
    }

    /// <summary>Checks explicit cancellation settles promptly and the signal can reload afterward.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CancelRevokesUncooperativeWorkAndAllowsReloadAsync()
    {
        var pending = new TaskCompletionSource<int>();
        int calls = 0;
        CancellationToken token = default;
        using AsyncSignal<int> signal = Signal.FromAsync(current =>
        {
            token = current;
            return ++calls == 1 ? pending.Task : Task.FromResult(9);
        });
        signal.Cancel();
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(AsyncSignalStatus.Canceled, (await signal.Completion).Status);
        Assert.Equal(AsyncSignalStatus.Canceled, signal.Value.Status);
        pending.SetResult(1);
        Assert.Equal(AsyncSignalStatus.Canceled, signal.Value.Status);
        Assert.Equal(9, (await signal.ReloadAsync()).Result);
        signal.Cancel();
        Assert.Equal(9, signal.Value.Result);
    }

    /// <summary>Checks disposal freezes readable state and never notifies surviving observers.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task DisposalRevokesPublicationWithoutNotifyingAsync()
    {
        var pending = new TaskCompletionSource<int>();
        AsyncSignal<int> signal = Signal.FromAsync(() => pending.Task);
        int notifications = 0;
        using SignalEffect effect = Signal.Effect(() =>
        {
            _ = signal.Value;
            notifications++;
        });
        AsyncSignalState<int> before = signal.Peek();
        signal.Dispose();
        signal.Dispose();
        pending.SetResult(7);
        Assert.Equal(AsyncSignalStatus.Canceled, (await signal.Completion).Status);
        Assert.Same(before, signal.Value);
        Assert.Equal(1, notifications);
        Assert.Throws<ObjectDisposedException>(() => { _ = signal.ReloadAsync(); });
        Assert.Throws<ObjectDisposedException>(signal.Cancel);
    }

    /// <summary>Checks scopes capture adapters even when setup fails, and leave shared values usable.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ScopeOwnsAsynchronousOperationsAndFailedSetupAsync()
    {
        var pending = new TaskCompletionSource<int>();
        var shared = Signal.Create(1);
        var scope = new SignalScope();
        AsyncSignal<int>? signal = null;
        CancellationToken token = default;
        Assert.Throws<ArithmeticException>(() => scope.Run(() =>
        {
            signal = Signal.FromAsync(current =>
            {
                token = current;
                return pending.Task;
            });
            _ = Signal.Effect(() => _ = shared.Value);
            throw new ArithmeticException("setup");
        }));
        scope.Dispose();
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(AsyncSignalStatus.Canceled, (await signal!.Completion).Status);
        shared.Value = 2;
        Assert.Equal(2, shared.Value);
        pending.SetResult(1);
        Assert.Equal(AsyncSignalStatus.Pending, signal.Value.Status);
    }

    /// <summary>Checks asynchronous conversion does not capture incidental loader reads as surrounding dependencies.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task LoaderReadsDoNotLeakIntoConstructionTrackingAsync()
    {
        var source = Signal.Create(1);
        int calls = 0;
        Task<int> Load()
        {
            calls++;
            return Task.FromResult(source.Value);
        }

        using AsyncSignal<int> signal = Signal.CaptureReads(() => Signal.FromAsync(Load), out SignalReadSet reads);
        Assert.True(reads.IsEmpty);
        source.Value = 2;
        Assert.Equal(1, calls);
        Assert.Equal(1, signal.Value.Result);
        Assert.Equal(2, (await signal.ReloadAsync()).Result);
    }

    /// <summary>Checks observer failures remain delivery errors instead of loader errors.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ObserverFailureDoesNotBecomeLoaderFailureAsync()
    {
        var pending = new TaskCompletionSource<int>();
        using AsyncSignal<int> signal = Signal.FromAsync(() => pending.Task);
        using SignalEffect effect = Signal.Effect(() =>
        {
            if (signal.Value.HasValue)
            {
                throw new ArithmeticException("observer");
            }
        });
        pending.SetResult(10);
        await Assert.ThrowsAsync<ArithmeticException>(() => signal.Completion);
        Assert.Equal(AsyncSignalStatus.Succeeded, signal.Value.Status);
        Assert.Null(signal.Value.Error);
    }

    /// <summary>Checks cancellation may reenter the adapter before a superseding loader starts.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ReentrantCancellationRevokesTheSupersedingOperationAsync()
    {
        var pending = new TaskCompletionSource<int>();
        AsyncSignal<int>? signal = null;
        int calls = 0;
        signal = Signal.FromAsync(token =>
        {
            calls++;
            token.Register(() => signal!.Cancel());
            return pending.Task;
        });
        using (signal)
        {
            Assert.Equal(AsyncSignalStatus.Canceled, (await signal.ReloadAsync()).Status);
            Assert.Equal(1, calls);
            Assert.Equal(AsyncSignalStatus.Canceled, signal.Value.Status);
            pending.SetResult(1);
        }
    }

    /// <summary>Checks scope cleanup continues after a cancellation callback throws.</summary>
    [Fact]
    public void ScopeCleanupContinuesAfterCancellationFailure()
    {
        var pending = new TaskCompletionSource<int>();
        var scope = new SignalScope();
        int canceled = 0;
        scope.Run(() =>
        {
            _ = Signal.FromAsync<int>(token =>
            {
                token.Register(() => throw new ArithmeticException("cancel"));
                return pending.Task;
            });
            _ = Signal.FromAsync<int>(token =>
            {
                token.Register(() => canceled++);
                return pending.Task;
            });
        });
        Assert.Throws<AggregateException>(scope.Dispose);
        Assert.Equal(1, canceled);
        scope.Dispose();
        pending.SetResult(1);
    }

    /// <summary>Checks asynchronous resources cannot be created from a pure computed evaluation.</summary>
    [Fact]
    public void PureEvaluationCannotStartAnAsynchronousOperation()
    {
        int calls = 0;
        Computed<AsyncSignal<int>> computed = Signal.Computed(() => Signal.FromAsync(() =>
        {
            calls++;
            return Task.FromResult(1);
        }));
        Assert.Throws<InvalidOperationException>(() => _ = computed.Value);
        Assert.Equal(0, calls);
    }

    /// <summary>Checks all asynchronous publication paths can synchronously deliver to a different thread.</summary>
    /// <param name="transition">The state transition to publish.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData("completion")]
    [InlineData("reload")]
    [InlineData("cancel")]
    public async Task PublicationReleasesTheGraphBeforeSchedulingAsync(string transition)
    {
        var pending = new TaskCompletionSource<int>();
        int calls = 0;
        using AsyncSignal<int> signal = Signal.FromAsync(() => ++calls == 1 && transition == "reload" ? Task.FromResult(1) : pending.Task);
        var scheduler = new SynchronousThreadScheduler();
        var statuses = new ConcurrentQueue<AsyncSignalStatus>();
        using SignalEffect effect = Signal.Effect(() => statuses.Enqueue(signal.Value.Status), scheduler);
        if (transition == "completion")
        {
            pending.SetResult(7);
            Assert.Equal(7, (await signal.Completion.WaitAsync(TimeSpan.FromSeconds(10))).Result);
        }
        else if (transition == "reload")
        {
            Task<AsyncSignalState<int>> completion = signal.ReloadAsync();
            Assert.Equal(AsyncSignalStatus.Pending, signal.Value.Status);
            pending.SetResult(7);
            Assert.Equal(7, (await completion.WaitAsync(TimeSpan.FromSeconds(10))).Result);
        }
        else
        {
            signal.Cancel();
            Assert.Equal(AsyncSignalStatus.Canceled, (await signal.Completion).Status);
            pending.SetResult(7);
        }

        Assert.Null(effect.Failure);
        Assert.True(scheduler.Deliveries > 0);
        Assert.Equal(signal.Value.Status, statuses.Last());
    }

    /// <summary>Checks cancel delivery failure is reported to both the caller and the captured completion.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CancelDeliveryFailureFaultsCompletionAsync()
    {
        var pending = new TaskCompletionSource<int>();
        using AsyncSignal<int> signal = Signal.FromAsync(() => pending.Task);
        var failure = new ArithmeticException("cancellation observer");
        using SignalEffect effect = Signal.Effect(() =>
        {
            if (signal.Value.Status == AsyncSignalStatus.Canceled)
            {
                pending.SetResult(7);
                throw failure;
            }
        });
        Task<AsyncSignalState<int>> completion = signal.Completion;
        Assert.Same(failure, Assert.Throws<ArithmeticException>(signal.Cancel));
        Assert.Same(failure, await Assert.ThrowsAsync<ArithmeticException>(() => completion));
        Assert.Equal(AsyncSignalStatus.Canceled, signal.Value.Status);
        Assert.Same(completion, signal.Completion);
    }

    private sealed class SynchronousThreadScheduler : ISignalScheduler
    {
        internal int Deliveries { get; private set; }

        public void Schedule(Action work)
        {
            Deliveries++;
            Task delivery = Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (!delivery.Wait(TimeSpan.FromSeconds(2)))
            {
                throw new TimeoutException("Signal delivery was blocked by the graph lock.");
            }
        }
    }
}
