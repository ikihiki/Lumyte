using Lumyte.Graphics.Native;
using Fixture = Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests.Fixture;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeTimelineTests
{
    [Fact]
    public void CpuSignalsExposeInitialAndFutureTimelineValues()
    {
        using var gpu = new Fixture();
        using NativeGpuSemaphore timeline = gpu.Backend.CreateSemaphore(5);
        Assert.True(timeline.IsComplete(5));
        Assert.False(timeline.IsComplete(9));

        timeline.SignalCpu(9);
        timeline.WaitCpu(9);

        Assert.True(timeline.IsComplete(9));
    }

    [Fact]
    public void AnEmptySubmissionCanWaitForAProducerSubmittedLaterToAnotherQueue()
    {
        using var gpu = new Fixture();
        NativeGpuQueue copy = Assert.IsAssignableFrom<NativeGpuQueue>(gpu.Backend.CopyQueue);
        using NativeGpuSemaphore ready = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore finished = gpu.Backend.CreateSemaphore();

        gpu.Backend.MainQueue.Submit([], new(finished, 1), [new(ready, 7)]);
        bool pendingBeforeProducer = !finished.IsComplete(1);
        copy.Submit([], new(ready, 7));
        finished.WaitCpu(1);

        Assert.True(pendingBeforeProducer);
        Assert.True(finished.IsComplete(1));
    }

    [Fact]
    public void SubmissionWaitsForEveryCopiedTimelinePoint()
    {
        using var gpu = new Fixture();
        NativeGpuQueue copy = Assert.IsAssignableFrom<NativeGpuQueue>(gpu.Backend.CopyQueue);
        using NativeGpuSemaphore first = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore second = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore finished = gpu.Backend.CreateSemaphore();
        NativeGpuTimelinePoint[] dependencies = [new(first, 2), new(second, 3)];
        copy.Submit([], new(finished, 1), dependencies);
        dependencies[0] = new(first, 91);
        dependencies[1] = new(second, 97);
        first.SignalCpu(2);
        try
        {
            Assert.False(finished.IsComplete(1));
        }
        finally
        {
            second.SignalCpu(3);
            finished.WaitCpu(1);
        }

        Assert.True(finished.IsComplete(1));
    }

    [Fact]
    public void CompletedCallerTimelineCanBeDestroyedBeforeQueueMemoryCollection()
    {
        using var gpu = new Fixture();
        NativeGpuQueue copy = Assert.IsAssignableFrom<NativeGpuQueue>(gpu.Backend.CopyQueue);
        NativeGpuSemaphore previous = gpu.Backend.CreateSemaphore();
        using NativeGpuCommandBuffer commands = copy.StartCommandRecording();
        copy.Submit([commands], new(previous, 1));
        previous.WaitCpu(1); // CPU wait does not collect the queue's command memory.
        previous.Dispose();
        using NativeGpuSemaphore next = gpu.Backend.CreateSemaphore();

        copy.Submit([], new(next, 1));
        next.WaitCpu(1);

        Assert.True(next.IsComplete(1));
    }
}
