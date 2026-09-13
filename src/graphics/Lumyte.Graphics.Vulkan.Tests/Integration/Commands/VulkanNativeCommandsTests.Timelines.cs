using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeCommandsTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void TimelineInitialValueIsImmediatelyObservable()
    {
        using var backend = VulkanBackend.Create();
        using var timeline = backend.CreateSemaphore(27);

        timeline.WaitCpu(27);

        Assert.True(timeline.IsComplete(27));
        Assert.False(timeline.IsComplete(28));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ExplicitCpuSignalAdvancesTheDeviceTimeline()
    {
        using var backend = VulkanBackend.Create();
        using var timeline = backend.CreateSemaphore(7);

        timeline.SignalCpu(19);
        timeline.WaitCpu(19);

        Assert.True(timeline.IsComplete(19));
        Assert.False(timeline.IsComplete(20));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void EmptySubmissionCanWaitForSeveralCpuSignals()
    {
        using var backend = VulkanBackend.Create();
        using var first = backend.CreateSemaphore();
        using var second = backend.CreateSemaphore();
        using var completion = backend.CreateSemaphore();
        backend.MainQueue.Submit([], new(completion, 1), [new(first, 1), new(second, 1)]);
        try
        {
            Assert.False(completion.IsComplete(1));
            first.SignalCpu(1);
            Assert.False(completion.IsComplete(1));
        }
        finally
        {
            if (!first.IsComplete(1)) { first.SignalCpu(1); }
            second.SignalCpu(1);
            completion.WaitCpu(1);
        }

        Assert.True(completion.IsComplete(1));
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void TimelinePointWithoutASemaphoreIsRejected(bool wait)
    {
        using var backend = VulkanBackend.Create();
        using var completion = backend.CreateSemaphore();

        var error = Assert.Throws<ArgumentNullException>(() =>
        {
            if (wait) { backend.MainQueue.Submit([], new(completion, 1), [default]); }
            else { backend.MainQueue.Submit([], default); }
        });

        Assert.Equal(wait ? "waits" : "signal", error.ParamName);
        Assert.False(completion.IsComplete(1));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void GpuWaitRejectsATimelineFromAnotherDeviceBeforeSubmission()
    {
        using var first = VulkanBackend.Create();
        using var second = VulkanBackend.Create();
        using var foreign = second.CreateSemaphore();
        using var completion = first.CreateSemaphore();

        var error = Assert.Throws<ArgumentException>(() => first.MainQueue.Submit([], new(completion, 1), [new(foreign, 1)]));

        Assert.Equal("waits", error.ParamName);
        Assert.False(completion.IsComplete(1));
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedTimelineCannotParticipateInASubmission(bool wait)
    {
        using var backend = VulkanBackend.Create();
        using var expired = backend.CreateSemaphore();
        using var completion = backend.CreateSemaphore();
        expired.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            if (wait) { backend.MainQueue.Submit([], new(completion, 1), [new(expired, 1)]); }
            else { backend.MainQueue.Submit([], new(expired, 1)); }
        });

        Assert.False(completion.IsComplete(1));
    }

    [VulkanCopyFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void CopyRecordingCannotBeSubmittedOnTheMainQueue()
    {
        using var backend = VulkanBackend.Create();
        using var completion = backend.CreateSemaphore();
        using var recording = backend.CopyQueue!.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => backend.MainQueue.Submit([recording], new(completion, 1)));

        Assert.Equal("commands", error.ParamName);
        Assert.False(completion.IsComplete(1));
    }

    [VulkanCopyFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DeviceTimelinesCanSignalAcrossBothQueuesInExecutionOrder()
    {
        using var backend = VulkanBackend.Create();
        using var timeline = backend.CreateSemaphore(6);

        backend.CopyQueue!.Submit([], new(timeline, 7));
        backend.MainQueue.Submit([], new(timeline, 8), [new(timeline, 7)]);
        timeline.WaitCpu(8);

        Assert.True(timeline.IsComplete(8));
    }

    [VulkanCopyFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void GpuWaitMayBeSubmittedBeforeAnotherQueuesSignal()
    {
        using var backend = VulkanBackend.Create();
        using var producer = backend.CreateSemaphore();
        using var completion = backend.CreateSemaphore();

        backend.MainQueue.Submit([], new(completion, 1), [new(producer, 1)]);
        try { Assert.False(completion.IsComplete(1)); }
        finally
        {
            backend.CopyQueue!.Submit([], new(producer, 1));
            completion.WaitCpu(1);
        }

        Assert.True(producer.IsComplete(1));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DeviceLossStopsBothQueuesAndCpuTimelineOperations()
    {
        using var backend = VulkanBackend.Create();
        var main = backend.MainQueue;
        var copy = backend.CopyQueue;
        using var timeline = backend.CreateSemaphore();

        Assert.Throws<GpuDeviceLostException>(() => backend.CheckDeviceResult(Result.ErrorDeviceLost, "test device-loss result"));

        Assert.Throws<GpuDeviceLostException>(() => main.StartCommandRecording());
        if (copy is not null) { Assert.Throws<GpuDeviceLostException>(() => copy.StartCommandRecording()); }
        Assert.Throws<GpuDeviceLostException>(() => timeline.IsComplete(0));
        Assert.Throws<GpuDeviceLostException>(() => timeline.WaitCpu(0));
        Assert.Throws<GpuDeviceLostException>(() => timeline.SignalCpu(1));
        Assert.Throws<GpuDeviceLostException>(() => backend.CreateSemaphore());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RejectedRecordingDoesNotEnqueueItsGpuWait()
    {
        using var backend = VulkanBackend.Create();
        using var gate = backend.CreateSemaphore();
        using var completion = backend.CreateSemaphore();
        using var foreign = new ForeignCommands();

        var error = Assert.Throws<ArgumentException>(() => backend.MainQueue.Submit([foreign], new(completion, 1), [new(gate, 1)]));
        backend.MainQueue.Submit([], new(completion, 1));
        completion.WaitCpu(1);

        Assert.Equal("commands", error.ParamName);
        Assert.False(gate.IsComplete(1));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void CommonDiscardUsesTheGeneralImageLayout()
    {
        using var resources = new Resources();
        var texture = resources.Texture(TextureDescription());
        var upload = resources.Linear(256, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        byte[] expected = Pattern(256, 93);
        expected.CopyTo(Bytes(upload));
        using var completion = resources.Backend.CreateSemaphore();
        using var commands = resources.Backend.MainQueue.StartCommandRecording();
        commands.DiscardTexture(View(texture), GpuTextureLayout.Common);
        commands.CopyMemoryToTexture(new(upload, 0, 256), texture, Footprint());
        CopyDependency(commands);
        commands.CopyTextureToMemory(texture, new(readback, 0, 256), Footprint());
        HostDependency(commands);

        resources.Backend.MainQueue.Submit([commands], new(completion, 1));
        completion.WaitCpu(1);

        Assert.Equal(expected, Bytes(readback).ToArray());
    }
}
