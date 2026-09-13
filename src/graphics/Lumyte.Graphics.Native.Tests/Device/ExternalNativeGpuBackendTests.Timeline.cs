namespace Lumyte.Graphics.Native.Tests.Device;

public sealed partial class ExternalNativeGpuBackendTests
{
    [Fact]
    public void ConsumerPassesOrderedGpuWaitsAndReusesTheirInputStorage()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        using NativeGpuSemaphore first = backend.CreateSemaphore();
        using NativeGpuSemaphore second = backend.CreateSemaphore();
        using NativeGpuSemaphore completion = backend.CreateSemaphore();
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
        var firstDependency = new NativeGpuTimelinePoint(first, (1ul << 50) + 3);
        var secondDependency = new NativeGpuTimelinePoint(second, (1ul << 48) + 9);
        var signal = new NativeGpuTimelinePoint(completion, (1ul << 52) + 7);
        NativeGpuTimelinePoint[] waits = [secondDependency, firstDependency];

        backend.MainQueue.Submit([commands], signal, waits);
        Array.Clear(waits);

        QueueSubmission submission = Assert.Single(observed.OfType<QueueSubmission>());
        Assert.Equal(signal, submission.Signal);
        Assert.Collection(submission.Waits,
            point => Assert.Equal(secondDependency, point),
            point => Assert.Equal(firstDependency, point));
    }

    [Fact]
    public void ConsumerSubmitsAnEmptyBatchWithATimelineDependency()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        using NativeGpuSemaphore producer = backend.CreateSemaphore();
        using NativeGpuSemaphore completion = backend.CreateSemaphore();
        var dependency = new NativeGpuTimelinePoint(producer, 2);
        var signal = new NativeGpuTimelinePoint(completion, 5);

        backend.MainQueue.Submit([], signal, [dependency]);

        QueueSubmission submission = Assert.Single(observed.OfType<QueueSubmission>());
        Assert.Empty(submission.Commands);
        Assert.Equal(signal, submission.Signal);
        Assert.Equal(dependency, Assert.Single(submission.Waits));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumerChoosesAnAvailableCopyQueueOrTheMainQueue(bool hasCopyQueue)
    {
        NativeGpuQueue? copyQueue = hasCopyQueue ? new ExternalQueue(null) : null;
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, copyQueue: copyQueue);

        NativeGpuQueue selected = backend.CopyQueue ?? backend.MainQueue;

        Assert.Same(copyQueue ?? backend.MainQueue, selected);
    }

    [Fact]
    public void ConsumerQueriesATimelineWithoutUsingAQueue()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        using NativeGpuSemaphore semaphore = backend.CreateSemaphore();
        const ulong value = (1ul << 55) + 1;

        bool complete = semaphore.IsComplete(value);

        Assert.False(complete);
        Assert.Equal(new CompletionQuery(semaphore, value), Assert.Single(observed.OfType<CompletionQuery>()));
    }

    [Fact]
    public void ConsumerWaitsForATimelineWithoutUsingAQueue()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        using NativeGpuSemaphore semaphore = backend.CreateSemaphore();
        const ulong value = (1ul << 55) + 2;

        semaphore.WaitCpu(value);

        Assert.Equal(new CompletionWait(semaphore, value), Assert.Single(observed.OfType<CompletionWait>()));
    }

    [Fact]
    public void ConsumerSignalsATimelineWithoutUsingAQueue()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        using NativeGpuSemaphore semaphore = backend.CreateSemaphore();
        const ulong value = (1ul << 55) + 3;

        semaphore.SignalCpu(value);

        Assert.Equal(new CompletionSignal(semaphore, value), Assert.Single(observed.OfType<CompletionSignal>()));
    }
}
