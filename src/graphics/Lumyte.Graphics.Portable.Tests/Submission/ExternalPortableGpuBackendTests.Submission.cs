namespace Lumyte.Graphics.Portable.Tests.Device;

public sealed partial class ExternalPortableGpuBackendTests
{
    [Fact]
    public async Task ConsumerSubmitsAndObservesAFullWidthValueOnTheSameTimeline()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        IGpuQueue queue = backend.MainQueue;
        using GpuSemaphore semaphore = queue.CreateSemaphore(1ul << 40);
        using GpuCommandBuffer commands = queue.StartCommandRecording();
        using CancellationTokenSource cancellation = new();
        const ulong value = (1ul << 40) + 7;

        queue.Submit([commands], semaphore, value);
        bool completed = queue.IsComplete(semaphore, value);
        await queue.WaitAsync(semaphore, value, cancellation.Token);

        Assert.False(completed);
        Assert.Collection(observed,
            item => Assert.Equal(new SemaphoreCreation(1ul << 40), item),
            item =>
            {
                var submission = Assert.IsType<SubmissionCall>(item);
                Assert.Same(commands, Assert.Single(submission.Commands));
                Assert.Equal(new GpuFenceValue(semaphore, value), submission.Point);
            },
            item => Assert.Equal(new CompletionQuery(new(semaphore, value)), item),
            item => Assert.Equal(new WaitCall(new(semaphore, value), cancellation.Token), item));
    }

    private sealed record SemaphoreCreation(ulong InitialValue);
    private sealed record SubmissionCall(GpuCommandBuffer[] Commands, GpuFenceValue Point);
    private sealed record CompletionQuery(GpuFenceValue Point);
    private sealed record WaitCall(GpuFenceValue Point, CancellationToken CancellationToken);

    // This spy checks the external implementation boundary, not native GPU completion semantics.
    private sealed class ExternalQueue(Action<object> observe) : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording() => new ExternalCommands(observe);
        public void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue)
            => observe(new SubmissionCall(commandBuffers.ToArray(), new(signalSemaphore, signalValue)));
        public GpuSemaphore CreateSemaphore(ulong initialValue = 0)
        { observe(new SemaphoreCreation(initialValue)); return new ExternalSemaphore(observe); }
        public bool IsComplete(GpuSemaphore semaphore, ulong value)
        { observe(new CompletionQuery(new(semaphore, value))); return false; }
        public ValueTask WaitAsync(GpuSemaphore semaphore, ulong value, CancellationToken cancellationToken = default)
        { observe(new WaitCall(new(semaphore, value), cancellationToken)); return ValueTask.CompletedTask; }
    }

    private sealed class ExternalSemaphore(Action<object> observe) : GpuSemaphore
    {
        public override void Dispose() => observe(this);
    }
}
