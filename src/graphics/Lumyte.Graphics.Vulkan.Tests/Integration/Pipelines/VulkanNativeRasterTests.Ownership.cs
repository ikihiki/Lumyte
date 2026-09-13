using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeRasterTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignRasterPipelineCannotBeSelected()
    {
        using var backend = VulkanBackend.Create();
        using var commands = backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.SetPipeline(new ForeignRasterPipeline()));

        Assert.Equal("pipeline", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignAttachmentCannotStartRendering()
    {
        using var backend = VulkanBackend.Create();
        using var commands = backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.BeginRendering([new(new ForeignView())]));

        Assert.Equal("view", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RejectedReadOnlyOperationsLeaveRecordingUsable()
    {
        using var r = new Resources();
        var target = r.Target(GpuFormat.D32Float);
        var readOnly = r.View(target.Texture, GpuFormat.D32Float, NativeGpuRenderViewFlags.DepthReadOnly);
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        Assert.Throws<ArgumentException>(() => commands.BeginRendering([], new(readOnly, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store)));
        commands.BeginRendering([], new(target.View, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearDepth: .625f));
        commands.EndRendering();
        ReadDepthStencil(commands, target.Texture, readback, NativeGpuTextureAspect.Depth);

        r.Submit(commands);

        Assert.Equal(.625f, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(Bytes(readback))[36]);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void CopyCannotSplitAnActiveRenderingScope()
    {
        using var r = new Resources();
        var target = r.Target();
        var memory = r.Linear(256, NativeGpuMemoryKind.GpuOnly);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);

        Assert.Throws<InvalidOperationException>(() => commands.CopyMemory(new(memory, 0, 4), new(memory, 64, 4)));

        commands.EndRendering();
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(false)]
    [InlineData(true)]
    public void IndirectDrawCannotReadBeyondItsLogicalRange(bool indexed)
    {
        using var r = new Resources();
        var target = r.Target();
        var memory = r.Linear(256, NativeGpuMemoryKind.GpuOnly);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);

        var error = Assert.Throws<ArgumentException>(() =>
        {
            if (indexed) { commands.DrawIndexedIndirect([], new(memory, 64, 12), NativeGpuIndexFormat.Uint16, new(memory, 0, 16)); }
            else { commands.DrawIndirect([], new(memory, 0, 12)); }
        });

        Assert.Equal("arguments", error.ParamName);
        commands.EndRendering();
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void AnOpenRenderingScopeCannotBeSubmitted()
    {
        using var r = new Resources();
        var target = r.Target();
        var queue = r.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);

        Assert.Throws<InvalidOperationException>(() => queue.Submit([commands], completion, 1));

        Assert.False(queue.IsComplete(completion, 1));
    }

    private sealed class ForeignRasterPipeline : NativeGpuRasterPipelineHandle;
    private sealed class ForeignView() : NativeGpuRenderViewHandle(NativeGpuRenderViewFlags.None);
}
