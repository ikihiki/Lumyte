using Lumyte.Graphics.Native;
using Fixture = Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests.Fixture;
using static Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeTimelineTests
{
    [Theory]
    [InlineData("SetEventOnCompletion", unchecked((int)0x887A0005))]
    [InlineData("ID3D12Fence.Signal", unchecked((int)0x887A0007))]
    public void CpuTimelineDeviceLossIsObservedByTheOtherQueueAndTimeline(string operation, int error)
    {
        using var gpu = new Fixture();
        NativeGpuQueue copy = Assert.IsAssignableFrom<NativeGpuQueue>(gpu.Backend.CopyQueue);
        using NativeGpuSemaphore timeline = gpu.Backend.CreateSemaphore();

        // Inject only the native-result boundary; no driver failure is claimed here.
        Assert.Throws<GpuDeviceLostException>(() => gpu.Backend.CheckDeviceResult(error, operation));

        Assert.Throws<GpuDeviceLostException>(() => copy.StartCommandRecording());
        Assert.Throws<GpuDeviceLostException>(() => timeline.IsComplete(0));
    }

    [Fact]
    public void CpuTimelineAllocationFailureDoesNotMarkTheDeviceLost()
    {
        using var gpu = new Fixture();
        using NativeGpuSemaphore timeline = gpu.Backend.CreateSemaphore();
        const int outOfMemory = unchecked((int)0x8007000E);

        NativeGpuException error = Assert.Throws<NativeGpuException>(() =>
            gpu.Backend.CheckDeviceResult(outOfMemory, "SetEventOnCompletion"));

        Assert.Equal(outOfMemory, error.NativeErrorCode);
        Assert.True(timeline.IsComplete(0));
    }

    [Fact]
    public void FailedBatchDoesNotInsertItsUnsatisfiedGpuWait()
    {
        using var gpu = new Fixture();
        Target target = gpu.Texture();
        NativeGpuRasterPipelineHandle invalid = gpu.Pipeline(vertex: [1, 2, 3, 4]);
        using NativeGpuSemaphore gate = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore failed = gpu.Backend.CreateSemaphore();
        using NativeGpuSemaphore drained = gpu.Backend.CreateSemaphore();
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(target.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(target.RenderView, NativeGpuLoadOp.Clear)]);
        commands.SetPipeline(invalid);
        commands.Draw([], 3);
        commands.EndRendering();

        NativeGpuException error = Assert.Throws<NativeGpuException>(() =>
            gpu.Backend.MainQueue.Submit([commands], new(failed, 1), [new(gate, 1)]));
        gpu.Backend.MainQueue.Submit([], new(drained, 1));
        drained.WaitCpu(1);

        Assert.Contains("CreateGraphicsPipelineState", error.Message);
        Assert.False(gate.IsComplete(1));
        Assert.False(failed.IsComplete(1));
    }

    [Fact]
    public void ARecordingCannotMoveBetweenTheDevicesMainAndCopyQueues()
    {
        using var gpu = new Fixture();
        NativeGpuQueue copy = Assert.IsAssignableFrom<NativeGpuQueue>(gpu.Backend.CopyQueue);
        using NativeGpuCommandBuffer recording = copy.StartCommandRecording();
        using NativeGpuSemaphore finished = gpu.Backend.CreateSemaphore();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            gpu.Backend.MainQueue.Submit([recording], new(finished, 1)));

        Assert.Equal("commands", error.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWaitMustBelongToTheSubmittingDeviceAndRemainAlive(bool disposed)
    {
        using var gpu = new Fixture();
        using var other = new Fixture();
        using NativeGpuSemaphore dependency = (disposed ? gpu : other).Backend.CreateSemaphore();
        using NativeGpuSemaphore finished = gpu.Backend.CreateSemaphore();
        if (disposed) { dependency.Dispose(); }

        if (disposed)
        {
            Assert.Throws<ObjectDisposedException>(() =>
                gpu.Backend.MainQueue.Submit([], new(finished, 1), [new(dependency, 1)]));
        }
        else
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                gpu.Backend.MainQueue.Submit([], new(finished, 1), [new(dependency, 1)]));
            Assert.Equal("waits", error.ParamName);
        }
    }

    [Theory]
    [InlineData("wait")]
    [InlineData("query")]
    [InlineData("signal")]
    public void DisposedTimelineRejectsCpuOperations(string operation)
    {
        using var gpu = new Fixture();
        NativeGpuSemaphore timeline = gpu.Backend.CreateSemaphore();
        timeline.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            switch (operation)
            {
                case "wait": timeline.WaitCpu(0); break;
                case "query": timeline.IsComplete(0); break;
                case "signal": timeline.SignalCpu(1); break;
            }
        });
    }
}
