using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;
using Silk.NET.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks native completion dispatch without timing-dependent assertions.</summary>
public sealed class VulkanCompletionTests
{
    /// <summary>Checks that queued waits share one non-pool worker and idle disposal drains publication.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task DispatcherSharesOneSleepingWorkerAsync()
    {
        using var dispatcher = new VulkanCompletionDispatcher();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool poolThread = true;
        dispatcher.Enqueue(() =>
        {
            poolThread = Thread.CurrentThread.IsThreadPoolThread;
            int thread = Environment.CurrentManagedThreadId;
            entered.Set();
            release.Wait();
            return () => first.SetResult(thread);
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            dispatcher.Enqueue(() =>
            {
                int thread = Environment.CurrentManagedThreadId;
                return () => second.SetResult(thread);
            });
            Assert.False(first.Task.IsCompleted);
            Assert.False(second.Task.IsCompleted);
            Assert.Throws<InvalidOperationException>(dispatcher.Dispose);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(await first.Task.WaitAsync(TimeSpan.FromSeconds(10)), await second.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(poolThread);
    }

    /// <summary>Checks caller cancellation cannot consume or cancel shared native completion.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CancellationLeavesNativeWaitRunningAsync()
    {
        using var dispatcher = new VulkanCompletionDispatcher();
        using var release = new ManualResetEventSlim();
        using var cancel = new CancellationTokenSource();
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Enqueue(() =>
        {
            release.Wait();
            return () => complete.SetResult();
        });
        try
        {
            Task canceled = complete.Task.WaitAsync(cancel.Token);
            Task survivor = complete.Task.WaitAsync(CancellationToken.None);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
            Assert.False(survivor.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        await complete.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Checks pending native waits, independent cancellation and disposal rejection before a controlled signal.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task PendingFenceWaitsUntilNativeSignalAsync()
    {
        using var device = VulkanDevice.Create();
        Fence fence = CreateFence(device);
        using var submission = new VulkanSubmission(device, [], fence);
        using var cancel = new CancellationTokenSource();
        Task canceled = submission.WaitAsync(cancel.Token).AsTask();
        Task[] survivors = Enumerable.Range(0, 8).Select(_ => submission.WaitAsync().AsTask()).ToArray();
        try
        {
            Assert.False(canceled.IsCompleted);
            Assert.All(survivors, task => Assert.False(task.IsCompleted));
            Assert.Equal(SubmissionStatus.Pending, submission.Status);
            Assert.Throws<InvalidOperationException>(submission.Dispose);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
            Assert.All(survivors, task => Assert.False(task.IsCompleted));
        }
        finally
        {
            SignalFence(device, fence);
            await Task.WhenAll(survivors).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(SubmissionStatus.Completed, submission.Status);
    }

    /// <summary>Checks concurrent public waits and reuse after an already canceled wait on a real device.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task SubmissionSupportsConcurrentWaitersAsync()
    {
        using var device = VulkanDevice.Create();
        for (int i = 0; i < 32; i++)
        {
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submission.WaitAsync(new CancellationToken(true)).AsTask());
            Task[] waits = Enumerable.Range(0, 8).Select(_ => Task.Run(async () => await submission.WaitAsync())).ToArray();
            await Task.WhenAll(waits).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(SubmissionStatus.Completed, submission.Status);
            Assert.Equal(CommandBufferState.Completed, commands.State);
            await submission.WaitAsync();
        }
    }

    /// <summary>Checks that loss subscribers can stop and join the native worker without self-joining.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task LossSubscriberCanDisposeDispatcherAsync()
    {
        using var dispatcher = new VulkanCompletionDispatcher();
        var lifetime = new GraphicsDeviceLifetime(new object());
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int worker = 0;
        lifetime.DeviceLost += (_, _) =>
        {
            try
            {
                Assert.NotEqual(worker, Environment.CurrentManagedThreadId);
                dispatcher.Dispose();
                notified.SetResult();
            }
            catch (Exception error)
            {
                notified.SetException(error);
            }
        };
        dispatcher.Enqueue(() =>
        {
            worker = Environment.CurrentManagedThreadId;
            return () => lifetime.ReportLossDeferred("test", "native loss");
        });
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(GraphicsDeviceStatus.Lost, lifetime.Status);
    }

    /// <summary>Checks reverse caller wait order cannot block an earlier fence behind a later pending fence.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task ReverseWaitOrderDoesNotBlockEarlierSubmissionAsync()
    {
        using var device = VulkanDevice.Create();
        Fence firstFence = CreateFence(device);
        using var first = new VulkanSubmission(device, [], firstFence);
        Fence secondFence = CreateFence(device);
        using var second = new VulkanSubmission(device, [], secondFence);
        Task later = second.WaitAsync().AsTask();
        Task earlier = first.WaitAsync().AsTask();
        SignalFence(device, firstFence);
        try
        {
            await earlier.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(later.IsCompleted);
        }
        finally
        {
            SignalFence(device, secondFence);
            await Task.WhenAll(earlier, later).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>Checks status-only completion can dispose while a native wait is retiring.</summary>
    [GpuFact]
    public void StatusOnlyCompletionSupportsDeviceDisposal()
    {
        using var device = VulkanDevice.Create();
        Fence fence = CreateFence(device);
        using var submission = new VulkanSubmission(device, [], fence);
        SignalFence(device, fence);
        device.WaitForRetirementAsync().GetAwaiter().GetResult();
        Assert.Equal(SubmissionStatus.Completed, submission.Status);
        submission.Dispose();
        device.Dispose();
    }

    /// <summary>Checks a cached loss cannot turn into successful wait completion when the native fence later signals.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task ObservedLossWinsOverNativeSuccessAsync()
    {
        using var device = VulkanDevice.Create();
        Fence fence = CreateFence(device);
        using var submission = new VulkanSubmission(device, [], fence);
        Task waiting = submission.WaitAsync().AsTask();
        device.ReportCompletionLoss("controlled loss observation");
        Assert.Equal(SubmissionStatus.Failed, submission.Status);
        SignalFence(device, fence);
        await Assert.ThrowsAsync<GraphicsDeviceLostException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<GraphicsDeviceLostException>(() => submission.WaitAsync().AsTask());
    }

    /// <summary>Checks a native wait error does not authorize destroying still-pending GPU work.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task FailedNativeWaitKeepsRetirementGuardAsync()
    {
        using var dispatcher = new VulkanCompletionDispatcher();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool retired = false;
        dispatcher.Enqueue(() => () => published.SetResult(), () => Volatile.Read(ref retired));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Throws<InvalidOperationException>(dispatcher.Dispose);
        Volatile.Write(ref retired, true);
        dispatcher.Dispose();
    }

    private static unsafe Fence CreateFence(VulkanDevice device)
    {
        var info = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Assert.Equal(Result.Success, device.Api.CreateFence(device.NativeDevice, &info, null, out Fence fence));
        return fence;
    }

    private static unsafe void SignalFence(VulkanDevice device, Fence fence)
        => Assert.Equal(Result.Success, device.Api.QueueSubmit(device.NativeQueue, 0, null, fence));
}
