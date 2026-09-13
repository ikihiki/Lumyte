using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
[Trait("Category", "VulkanNativeConformance")]
public sealed class VulkanNativeAsyncSemaphoreTests
{
    [VulkanNativeFact]
    public async Task PendingGpuWaitRetainsTheSemaphoreUntilTheProducerSignals()
    {
        using var backend = VulkanBackend.Create();
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

    [VulkanNativeFact]
    public async Task CancelingAnUnsignaledWaitAllowsTheCpuOnlySemaphoreToBeDestroyed()
    {
        using var backend = VulkanBackend.Create();
        using var semaphore = backend.CreateSemaphore();
        using var cancellation = new CancellationTokenSource();
        Task pending = semaphore.WaitAsync(9, cancellation.Token).AsTask();

        cancellation.Cancel();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        semaphore.Dispose();

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => semaphore.IsComplete(0));
    }

    [VulkanNativeFact]
    public async Task PendingObservationReportsSharedDeviceFailureWithoutCompleting()
    {
        using var backend = VulkanBackend.Create();
        using var semaphore = backend.CreateSemaphore();
        Task pending = semaphore.WaitAsync(1).AsTask();

        // Exercise the native-result boundary without losing the real device or submitting GPU work.
        Assert.Throws<GpuDeviceLostException>(() =>
            backend.CheckDeviceResult(Result.ErrorDeviceLost, "injected timeline observation"));

        await Assert.ThrowsAsync<GpuDeviceLostException>(() => pending);
        Assert.False(pending.IsCompletedSuccessfully);
    }
}
