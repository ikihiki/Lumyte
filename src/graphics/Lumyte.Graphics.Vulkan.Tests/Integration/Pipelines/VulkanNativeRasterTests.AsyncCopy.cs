using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeRasterTests
{
    [VulkanCopyFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MainDrawSamplesAColorTextureUploadedByTheCopyQueue()
    {
        using var r = new Resources();
        var target = r.Target();
        var sampled = r.Target();
        var pipeline = r.Pipeline(pixel: "NativeSampledPixel.spv", pixelEntry: "sampledMain");
        var positions = r.Positions();
        var upload = r.Linear(256, NativeGpuMemoryKind.CpuVisible);
        for (int i = 0; i < 64; i++) { new byte[] { 37, 113, 211, 255 }.CopyTo(Bytes(upload)[(i * 4)..]); }
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        var heap = r.Descriptors(NativeGpuDescriptorHeapKind.Resource, 8);
        var samplers = r.Descriptors(NativeGpuDescriptorHeapKind.Sampler, 8);
        r.Backend.WriteTextureDescriptor(heap, 5, new(sampled.Texture, NativeGpuTextureViewDimension.TwoD,
            GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1));
        r.Backend.WriteSamplerDescriptor(samplers, 2, new());
        using var uploaded = r.Backend.CreateSemaphore();
        using var rendered = r.Backend.CreateSemaphore();
        var copy = r.Backend.CopyQueue!;
        using var transfer = copy.StartCommandRecording();
        transfer.CopyMemoryToTexture(new(upload, 0, 256), sampled.Texture,
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(8, 8, 1), 32, 256));
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.SetResourceDescriptorHeap(heap);
        commands.SetSamplerDescriptorHeap(samplers);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);
        commands.Draw(Root(positions.GpuAddress, default, texture: 5, sampler: 2), 3);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        // The first image initialization is accepted on the producer before the consumer Submit.
        copy.Submit([transfer], new(uploaded, 1));
        try
        {
            r.Backend.MainQueue.Submit([commands], new(rendered, 1), [new(uploaded, 1)]);
            rendered.WaitCpu(1);
        }
        finally { uploaded.WaitCpu(1); }

        Assert.Equal(new byte[] { 37, 113, 211, 255 }, Pixel(readback, 4, 4));
    }

    [VulkanCopyFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ColorTextureCanReturnFromMainToCopyWithoutOwnershipTracking()
    {
        using var r = new Resources();
        var target = r.Target();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var rendered = r.Backend.CreateSemaphore();
        using var copied = r.Backend.CreateSemaphore();
        var copy = r.Backend.CopyQueue!;
        using var draw = r.Backend.MainQueue.StartCommandRecording();
        draw.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 1, 0, 1))]);
        draw.EndRendering();
        using var transfer = copy.StartCommandRecording();
        transfer.CopyTextureToMemory(target.Texture, new(readback, 0, 256),
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(8, 8, 1), 32, 256));
        transfer.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        r.Backend.MainQueue.Submit([draw], new(rendered, 1));
        try
        {
            copy.Submit([transfer], new(copied, 1), [new(rendered, 1)]);
            copied.WaitCpu(1);
        }
        finally { rendered.WaitCpu(1); }

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(readback, 4, 4));
    }
}
