namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed class VulkanNativeTimelineConcurrencyTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void CpuTimelineWaitCanOverlapQueueSubmission()
    {
        using var backend = VulkanBackend.Create();
        using var timeline = backend.CreateSemaphore();
        using var enteringWait = new ManualResetEventSlim();
        Exception? waitError = null;
        var waiter = new Thread(() =>
        {
            try
            {
                enteringWait.Set();
                timeline.WaitCpu(1);
            }
            catch (Exception exception) { waitError = exception; }
        });
        waiter.Start();
        enteringWait.Wait();
        bool submitted = false;
        try
        {
            backend.MainQueue.Submit([], new(timeline, 1));
            submitted = true;
        }
        finally
        {
            if (!submitted) { timeline.SignalCpu(1); }
            waiter.Join();
        }

        Assert.Null(waitError);
        Assert.True(timeline.IsComplete(1));
    }

    [VulkanCopyFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DistinctQueuesCanSubmitConcurrentlyBehindACpuGate()
    {
        using var backend = VulkanBackend.Create();
        var main = backend.MainQueue;
        var copy = backend.CopyQueue!;
        using var gate = backend.CreateSemaphore();
        using var mainCompleted = backend.CreateSemaphore();
        using var copyCompleted = backend.CreateSemaphore();
        bool mainSubmitted = false;
        bool copySubmitted = false;
        try
        {
            RunTogether(
                () => { main.Submit([], new(mainCompleted, 1), [new(gate, 1)]); mainSubmitted = true; },
                () => { copy.Submit([], new(copyCompleted, 1), [new(gate, 1)]); copySubmitted = true; });

            Assert.False(mainCompleted.IsComplete(1));
            Assert.False(copyCompleted.IsComplete(1));
        }
        finally
        {
            gate.SignalCpu(1);
            if (mainSubmitted) { mainCompleted.WaitCpu(1); }
            if (copySubmitted) { copyCompleted.WaitCpu(1); }
        }
    }

    private static void RunTogether(params Action[] actions)
    {
        Exception?[] errors = new Exception?[actions.Length];
        using var start = new Barrier(actions.Length);
        Thread[] workers = actions.Select((action, index) => new Thread(() =>
        {
            try { start.SignalAndWait(); action(); }
            catch (Exception exception) { errors[index] = exception; }
        })).ToArray();
        foreach (Thread worker in workers) { worker.Start(); }
        foreach (Thread worker in workers) { worker.Join(); }
        var failures = errors.OfType<Exception>().ToArray();
        if (failures.Length != 0) { throw new AggregateException(failures); }
    }
}
