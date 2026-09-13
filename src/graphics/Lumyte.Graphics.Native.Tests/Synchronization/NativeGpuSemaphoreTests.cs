namespace Lumyte.Graphics.Native.Tests;

public sealed class NativeGpuSemaphoreTests
{
    [Fact]
    public async Task FutureValueCanBeSignaledAfterStartingAnAsynchronousWait()
    {
        using var semaphore = new TestSemaphore();

        ValueTask pending = semaphore.WaitAsync(ulong.MaxValue);
        Assert.False(pending.IsCompleted);
        semaphore.SignalCpu(ulong.MaxValue);
        await pending;

        Assert.True(semaphore.IsComplete(ulong.MaxValue));
    }

    [Fact]
    public void ReachedValueCompletesWithoutSchedulingAWait()
    {
        using var semaphore = new TestSemaphore();
        semaphore.SignalCpu(41);

        ValueTask wait = semaphore.WaitAsync(41);

        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public void AlreadyCanceledWaitDoesNotQueryTheSemaphore()
    {
        using var semaphore = new TestSemaphore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = Assert.Throws<OperationCanceledException>(() =>
        { _ = semaphore.WaitAsync(1, cancellation.Token); });

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(0, semaphore.QueryCount);
    }

    [Fact]
    public async Task CancellationEndsOnlyTheSelectedWait()
    {
        using var semaphore = new TestSemaphore();
        using var cancellation = new CancellationTokenSource();
        Task canceled = semaphore.WaitAsync(7, cancellation.Token).AsTask();
        Task retained = semaphore.WaitAsync(7).AsTask();

        cancellation.Cancel();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.False(retained.IsCompleted);
        Assert.False(semaphore.IsComplete(7));
        semaphore.SignalCpu(7);
        await retained;
    }

    [Fact]
    public async Task ObservationFailureDoesNotBecomeSuccessfulCompletion()
    {
        using var semaphore = new TestSemaphore();
        Task pending = semaphore.WaitAsync(17).AsTask();
        var expected = new InvalidOperationException("The completion observation was lost.");

        semaphore.Fail(expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Same(expected, actual);
        Assert.False(pending.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CancellationFinishesAllQueriesBeforeReturning()
    {
        using var semaphore = new TestSemaphore();
        using var cancellation = new CancellationTokenSource();
        Task pending = semaphore.WaitAsync(1, cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        semaphore.Dispose();

        Assert.True(pending.IsCanceled);
    }

    private sealed class TestSemaphore : NativeGpuSemaphore
    {
        private readonly object gate = new();
        private ulong current;
        private Exception? failure;
        private bool disposed;
        private int queries;

        internal int QueryCount { get { lock (gate) { return queries; } } }

        public override bool IsComplete(ulong value)
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                queries++;
                if (failure is not null) { throw failure; }
                return current >= value;
            }
        }

        public override void WaitCpu(ulong value)
            => throw new InvalidOperationException("An asynchronous observation must not invoke a blocking CPU wait.");

        public override void SignalCpu(ulong value) { lock (gate) { current = value; } }
        internal void Fail(Exception error) { lock (gate) { failure = error; } }
        public override void Dispose() { lock (gate) { disposed = true; } }
    }
}
