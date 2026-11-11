using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Checks event-based native fence completion with explicitly controlled signal timing.</summary>
public sealed class SubmissionCompletionTests
{
    /// <summary>Checks one caller can cancel without canceling other waiters or consuming the fence event.</summary>
    /// <returns>The asynchronous verification.</returns>
    [AgilityFact]
    public async Task PendingWaitsShareSignalAndCancelIndependentlyAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var device = Direct3D12Device.Create(new() { UseWarp = true, EnableDebugLayer = true });
        device.Check(NativeMethods.FenceCreate(device.Native, out nint fence));
        using var submission = new Direct3D12Submission(device, [], fence);
        using var cancel = new CancellationTokenSource();
        Task canceled = submission.WaitAsync(cancel.Token).AsTask();
        Task[] survivors = Enumerable.Range(0, 8).Select(_ => submission.WaitAsync().AsTask()).ToArray();
        try
        {
            Assert.False(canceled.IsCompleted);
            Assert.All(survivors, task => Assert.False(task.IsCompleted));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
            Assert.All(survivors, task => Assert.False(task.IsCompleted));
            Assert.Equal(SubmissionStatus.Pending, submission.Status);
        }
        finally
        {
            device.Check(NativeMethods.QueueSignal(device.Native, fence));
            await Task.WhenAll(survivors).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(SubmissionStatus.Completed, submission.Status);
        await submission.WaitAsync();
    }

    /// <summary>Checks disposal of the submission cannot free event/fence storage still owned by a native wait.</summary>
    /// <returns>The asynchronous verification.</returns>
    [AgilityFact]
    public async Task NativeWaitRetainsFenceAfterSubmissionDisposalAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var device = Direct3D12Device.Create(new() { UseWarp = true, EnableDebugLayer = true });
        device.Check(NativeMethods.FenceCreate(device.Native, out nint fence));
        var submission = new Direct3D12Submission(device, [], fence);
        Task waiting = submission.WaitAsync().AsTask();
        submission.Dispose();

        // The completion registration owns its own COM reference even though
        // the submission has released its reference. Signal that retained fence.
        device.Check(NativeMethods.QueueSignal(device.Native, fence));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(SubmissionStatus.Disposed, submission.Status);
        submission.Dispose();
    }
}
