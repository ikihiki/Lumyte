using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeCommandsTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignRecordingIsRejected()
    {
        using var resources = new Resources();
        using var recording = new ForeignCommands();
        using var completion = resources.Backend.MainQueue.CreateSemaphore(0);

        var error = Assert.Throws<ArgumentException>(() => resources.Backend.MainQueue.Submit([recording], completion, 1));

        Assert.Equal("commands", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RecordingFromAnotherDeviceIsRejected()
    {
        using var first = new Resources();
        using var second = new Resources();
        using var recording = second.Backend.MainQueue.StartCommandRecording();
        using var completion = first.Backend.MainQueue.CreateSemaphore(0);

        var error = Assert.Throws<ArgumentException>(() => first.Backend.MainQueue.Submit([recording], completion, 1));

        Assert.Equal("commands", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignSemaphoreIsRejected()
    {
        using var resources = new Resources();
        using var semaphore = new ForeignSemaphore();

        var error = Assert.Throws<ArgumentException>(() => resources.Backend.MainQueue.IsComplete(semaphore, 1));

        Assert.Equal("semaphore", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void SemaphoreFromAnotherDeviceIsRejected()
    {
        using var first = new Resources();
        using var second = new Resources();
        using var semaphore = second.Backend.MainQueue.CreateSemaphore(0);

        var error = Assert.Throws<ArgumentException>(() => first.Backend.MainQueue.IsComplete(semaphore, 1));

        Assert.Equal("semaphore", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignLinearRangeIsRejected()
    {
        using var resources = new Resources();
        using var commands = resources.Backend.MainQueue.StartCommandRecording();
        NativeGpuRange range = new(new ForeignRegion(), 0, 16);

        var error = Assert.Throws<ArgumentException>(() => commands.CopyMemory(range, range));

        Assert.Equal("range", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void LinearRangeFromAnotherDeviceIsRejected()
    {
        using var first = new Resources();
        using var second = new Resources();
        var region = second.Linear(256, NativeGpuMemoryKind.GpuOnly);
        using var commands = first.Backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.CopyMemory(new(region, 0, 16), new(region, 32, 16)));

        Assert.Equal("range", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignTextureIsRejected()
    {
        using var resources = new Resources();
        using var commands = resources.Backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.DiscardTexture(View(new ForeignTexture()), GpuTextureLayout.General));

        Assert.Equal("texture", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void TextureFromAnotherDeviceIsRejected()
    {
        using var first = new Resources();
        using var second = new Resources();
        var texture = second.Texture(TextureDescription());
        using var commands = first.Backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.DiscardTexture(View(texture), GpuTextureLayout.General));

        Assert.Equal("texture", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void CopyRejectsDestinationSmallerThanTransferredBytes()
    {
        using var resources = new Resources();
        var region = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        using var commands = resources.Backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.CopyMemory(new(region, 0, 64), new(region, 128, 32)));

        Assert.Equal("destination", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DisposedRecordingRejectsFurtherCommands()
    {
        using var resources = new Resources();
        using var commands = resources.Backend.MainQueue.StartCommandRecording();
        commands.Dispose();

        Assert.Throws<ObjectDisposedException>(() => commands.Barrier(GpuStage.None, GpuAccess.None, GpuStage.None, GpuAccess.None));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DisposedSemaphoreCannotBeQueried()
    {
        using var resources = new Resources();
        using var semaphore = resources.Backend.MainQueue.CreateSemaphore(0);
        semaphore.Dispose();

        Assert.Throws<ObjectDisposedException>(() => resources.Backend.MainQueue.IsComplete(semaphore, 0));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ExplicitTextureTransitionReportsUnsupportedCapability()
    {
        using var resources = new Resources();
        var texture = resources.Texture(TextureDescription());
        using var commands = resources.Backend.MainQueue.StartCommandRecording();

        Assert.Throws<NotSupportedException>(() => commands.TextureTransition(View(texture), GpuTextureLayout.General, GpuTextureLayout.CopySource));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void SeparateRecordingsCanBeDisposedInParallel()
    {
        using var resources = new Resources();
        var queue = resources.Backend.MainQueue;
        var recordings = Enumerable.Range(0, 32).Select(_ => queue.StartCommandRecording()).ToArray();

        Parallel.ForEach(recordings, recording =>
        {
            recording.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead);
            recording.Dispose();
        });

        Assert.All(recordings, recording => Assert.Throws<ObjectDisposedException>(
            () => recording.Barrier(GpuStage.None, GpuAccess.None, GpuStage.None, GpuAccess.None)));
    }

    private sealed class ForeignHeap() : NativeGpuHeap(4096, 256, NativeGpuMemoryKind.GpuOnly);
    private sealed class ForeignRegion() : NativeGpuLinearRegion(new ForeignHeap(), 0, 256, 4096, 0);
    private sealed class ForeignTexture : NativeGpuTextureHandle;
    private sealed class ForeignSemaphore : NativeGpuSemaphore { public override void Dispose() { } }
    private sealed class ForeignCommands : NativeGpuCommandBuffer
    {
        public override void SetResourceDescriptorHeap(NativeGpuDescriptorHeap heap) => throw new NotSupportedException();
        public override void SetSamplerDescriptorHeap(NativeGpuDescriptorHeap heap) => throw new NotSupportedException();
        public override void CopyMemory(NativeGpuRange source, NativeGpuRange destination) => throw new NotSupportedException();
        public override void CopyMemoryToTexture(NativeGpuRange source, NativeGpuTextureHandle destination, NativeGpuTextureCopyFootprint footprint) => throw new NotSupportedException();
        public override void CopyTextureToMemory(NativeGpuTextureHandle source, NativeGpuRange destination, NativeGpuTextureCopyFootprint footprint) => throw new NotSupportedException();
        public override void Barrier(GpuStage beforeStages, GpuAccess beforeAccess, GpuStage afterStages, GpuAccess afterAccess) => throw new NotSupportedException();
        public override void TextureTransition(NativeGpuTextureView view, GpuTextureLayout beforeLayout, GpuTextureLayout afterLayout) => throw new NotSupportedException();
        public override void DiscardTexture(NativeGpuTextureView view, GpuTextureLayout afterLayout) => throw new NotSupportedException();
        public override void Dispose() { }
    }
}
