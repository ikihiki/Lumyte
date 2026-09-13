namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed class DirectX12NativeAsyncSemaphoreTests
{
    [Fact]
    public async Task PendingGpuWaitRetainsTheSemaphoreUntilTheProducerSignals()
    {
        using var backend = DirectX12Backend.Create();
        using var gate = backend.CreateSemaphore();
        using var completed = backend.CreateSemaphore();
        backend.MainQueue.Submit([], new(completed, 1), [new(gate, 1)]);
        Task pending = completed.WaitAsync(1).AsTask();
        try
        {
            Assert.False(pending.IsCompleted);
            var failure = Assert.Throws<InvalidOperationException>(completed.Dispose);
            Assert.Contains("active asynchronous CPU wait", failure.Message);
        }
        finally
        {
            gate.SignalCpu(1);
            await pending;
        }

        Assert.True(completed.IsComplete(1));
    }

    [Fact]
    public async Task CancelingAnUnsignaledWaitAllowsTheCpuOnlySemaphoreToBeDestroyed()
    {
        using var backend = DirectX12Backend.Create();
        using var semaphore = backend.CreateSemaphore();
        using var cancellation = new CancellationTokenSource();
        Task pending = semaphore.WaitAsync(9, cancellation.Token).AsTask();

        cancellation.Cancel();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        semaphore.Dispose();

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => semaphore.IsComplete(0));
    }

    [Fact]
    public async Task PendingObservationReportsSharedDeviceFailureWithoutCompleting()
    {
        using var backend = DirectX12Backend.Create();
        using var semaphore = backend.CreateSemaphore();
        Task pending = semaphore.WaitAsync(1).AsTask();

        // Exercise the native-result boundary without removing the real device or submitting GPU work.
        Assert.Throws<GpuDeviceLostException>(() =>
            backend.CheckDeviceResult(unchecked((int)0x887A0005), "injected timeline observation"));

        await Assert.ThrowsAsync<GpuDeviceLostException>(() => pending);
        Assert.False(pending.IsCompletedSuccessfully);
    }
}
