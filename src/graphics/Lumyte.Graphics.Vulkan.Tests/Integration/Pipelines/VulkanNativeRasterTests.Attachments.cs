using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeRasterTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void FixedBlendAndWriteMaskSurviveMutationOfTheInputArray()
    {
        using var r = new Resources();
        var target = r.Target();
        NativeGpuColorTargetDescription[] colorTargets = [new(GpuFormat.Rgba8Unorm,
            GpuColorWriteMask.Red | GpuColorWriteMask.Green | GpuColorWriteMask.Blue,
            new(Enabled: true, SourceColorFactor: NativeGpuBlendFactor.SourceAlpha,
                DestinationColorFactor: NativeGpuBlendFactor.OneMinusSourceAlpha))];
        var pipeline = r.Pipeline(new() { ColorTargets = colorTargets });
        colorTargets[0] = new(GpuFormat.Rgba8Unorm);
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 1, 1))]);
        commands.SetPipeline(pipeline);
        commands.Draw(Root(positions.GpuAddress, new(1, 0, 0, .5f)), 3);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        // UNORM8 blend precision is bounded by the destination representation. The .5 midpoint
        // may quantize to either neighbor (this device produces red 127 and blue 128).
        // https://docs.vulkan.org/spec/latest/chapters/framebuffer.html#framebuffer-blending
        Assert.Collection(Pixel(readback, 4, 4),
            red => Assert.InRange(red, (byte)127, (byte)128),
            green => Assert.Equal(0, green),
            blue => Assert.InRange(blue, (byte)127, (byte)128),
            alpha => Assert.Equal(255, alpha));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void FragmentShaderSamplesSelectedHeapsAfterAttachmentInitialization()
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
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.SetResourceDescriptorHeap(heap);
        commands.SetSamplerDescriptorHeap(samplers);
        commands.CopyMemoryToTexture(new(upload, 0, 256), sampled.Texture,
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(8, 8, 1), 32, 256));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.PixelShader, GpuAccess.ShaderRead);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);
        commands.Draw(Root(positions.GpuAddress, default, texture: 5, sampler: 2), 3);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 37, 113, 211, 255 }, Pixel(readback, 4, 4));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DynamicDepthStateChangesWithoutCreatingAnotherPipeline()
    {
        using var r = new Resources();
        var target = r.Target();
        var depth = r.Target(GpuFormat.D32Float);
        var pipeline = r.Pipeline(new() { ColorTargets = [new(GpuFormat.Rgba8Unorm)], DepthStencilFormat = GpuFormat.D32Float });
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.SetPipeline(pipeline);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)],
            new(depth.View, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store));
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Less));
        commands.Draw(Root(positions.GpuAddress, new(0, 1, 0, 1), depth: .25f), 3);
        commands.Draw(Root(positions.GpuAddress, new(1, 0, 0, 1), depth: .75f), 3);
        commands.SetScissor(new(4, 0, 4, 8));
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: false, DepthCompare: GpuCompareOp.Greater));
        commands.Draw(Root(positions.GpuAddress, new(0, 0, 1, 1), depth: .75f), 3);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(readback, 2, 4));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(readback, 6, 4));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ReadOnlyDepthAttachmentPreservesDepthWhileTestingFragments()
    {
        using var r = new Resources();
        var target = r.Target();
        var depth = r.Target(GpuFormat.D32Float);
        var readOnly = r.View(depth.Texture, GpuFormat.D32Float, NativeGpuRenderViewFlags.DepthReadOnly);
        var pipeline = r.Pipeline(new() { ColorTargets = [new(GpuFormat.Rgba8Unorm)], DepthStencilFormat = GpuFormat.D32Float });
        var positions = r.Positions();
        var colors = r.Linear(256, NativeGpuMemoryKind.Readback);
        var depths = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([], new(depth.View, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearDepth: .25f));
        commands.EndRendering();
        commands.Barrier(GpuStage.DepthStencil, GpuAccess.DepthStencilWrite, GpuStage.DepthStencil, GpuAccess.DepthStencilRead);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)], new(readOnly));
        commands.SetPipeline(pipeline);
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: false, DepthCompare: GpuCompareOp.Less));
        commands.Draw(Root(positions.GpuAddress, new(0, 1, 0, 1), depth: .125f), 3);
        commands.Draw(Root(positions.GpuAddress, new(1, 0, 0, 1), depth: .5f), 3);
        commands.EndRendering();
        ReadColor(commands, target.Texture, colors);
        ReadDepthStencil(commands, depth.Texture, depths, NativeGpuTextureAspect.Depth);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(colors, 4, 4));
        Assert.Equal(.25f, MemoryMarshal.Cast<byte, float>(Bytes(depths))[36]);
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(NativeGpuFrontFace.CounterClockwise, 5)]
    [InlineData(NativeGpuFrontFace.Clockwise, 9)]
    public void ReadOnlyStencilLoadsTheStoredReferenceWithoutWriting(NativeGpuFrontFace frontFace, byte expectedStencil)
    {
        using var r = new Resources();
        var target = r.Target();
        var depth = r.Target(GpuFormat.Depth24PlusStencil8);
        var readOnly = r.View(depth.Texture, GpuFormat.Depth24PlusStencil8,
            NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly);
        var pipeline = r.Pipeline(new() { ColorTargets = [new(GpuFormat.Rgba8Unorm)], DepthStencilFormat = GpuFormat.Depth24PlusStencil8, FrontFace = frontFace });
        var positions = r.Positions();
        var colors = r.Linear(256, NativeGpuMemoryKind.Readback);
        var stencils = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)], new(depth.View,
            NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store));
        commands.SetPipeline(pipeline);
        commands.SetDepthStencilState(new(StencilTest: true,
            Front: new(PassOp: NativeGpuStencilOperation.Replace, Reference: 5),
            Back: new(PassOp: NativeGpuStencilOperation.Replace, Reference: 9)));
        commands.Draw(Root(positions.GpuAddress, new(1, 0, 0, 1)), 3);
        commands.EndRendering();
        commands.Barrier(GpuStage.ColorOutput | GpuStage.DepthStencil, GpuAccess.ColorWrite | GpuAccess.DepthStencilWrite,
            GpuStage.ColorOutput | GpuStage.DepthStencil, GpuAccess.ColorRead | GpuAccess.ColorWrite | GpuAccess.DepthStencilRead);
        commands.BeginRendering([new(target.View)], new(readOnly));
        commands.SetDepthStencilState(new(StencilTest: true, StencilWriteMask: 0,
            Front: new(Compare: GpuCompareOp.Equal, Reference: 5), Back: new(Compare: GpuCompareOp.Equal, Reference: 9)));
        commands.Draw(Root(positions.GpuAddress, new(0, 0, 1, 1)), 3);
        commands.EndRendering();
        ReadColor(commands, target.Texture, colors);
        ReadDepthStencil(commands, depth.Texture, stencils, NativeGpuTextureAspect.Stencil);

        r.Submit(commands);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(colors, 4, 4));
        Assert.Equal(expectedStencil, Bytes(stencils)[36]);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void VertexOnlyPipelineWritesDepth()
    {
        using var r = new Resources();
        var target = r.Target(GpuFormat.D32Float);
        var pipeline = r.Pipeline(new() { DepthStencilFormat = GpuFormat.D32Float }, pixel: null);
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([], new(target.View, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store));
        commands.SetPipeline(pipeline);
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Always));
        commands.Draw(Root(positions.GpuAddress, default, depth: .375f), 3);
        commands.EndRendering();
        ReadDepthStencil(commands, target.Texture, readback, NativeGpuTextureAspect.Depth);

        r.Submit(commands);

        Assert.Equal(.375f, MemoryMarshal.Cast<byte, float>(Bytes(readback))[36]);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void AllFreshAttachmentsInitializeBeforeTheRenderingScope()
    {
        using var r = new Resources();
        var first = r.Target();
        var second = r.Target();
        var firstPixels = r.Linear(256, NativeGpuMemoryKind.Readback);
        var secondPixels = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([
            new(first.View, NativeGpuLoadOp.Clear, ClearColor: new(1, 0, 0, 1)),
            new(second.View, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 1, 1))]);
        commands.EndRendering();
        ReadColor(commands, first.Texture, firstPixels);
        ReadColor(commands, second.Texture, secondPixels);

        r.Submit(commands);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(firstPixels, 4, 4));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(secondPixels, 4, 4));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RenderingResetsDepthStencilViewportAndScissor()
    {
        using var r = new Resources();
        var target = r.Target();
        var pipeline = r.Pipeline();
        var positions = r.Positions();
        var readback = r.Linear(256, NativeGpuMemoryKind.Readback);
        using var commands = r.Backend.MainQueue.StartCommandRecording();
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Clear)]);
        commands.SetViewport(new(0, 0, 2, 2));
        commands.SetScissor(new(0, 0, 2, 2));
        commands.SetDepthStencilState(new(StencilTest: true, Front: new(Compare: GpuCompareOp.Never), Back: new(Compare: GpuCompareOp.Never)));
        commands.EndRendering();
        commands.Barrier(GpuStage.ColorOutput, GpuAccess.ColorWrite, GpuStage.ColorOutput, GpuAccess.ColorWrite);
        commands.BeginRendering([new(target.View, NativeGpuLoadOp.Discard)]);
        commands.SetPipeline(pipeline);
        commands.Draw(Root(positions.GpuAddress, new(1, 1, 0, 1)), 3);
        commands.EndRendering();
        ReadColor(commands, target.Texture, readback);

        r.Submit(commands);

        Assert.Equal(new byte[] { 255, 255, 0, 255 }, Pixel(readback, 6, 6));
    }

    private static void ReadDepthStencil(NativeGpuCommandBuffer commands, NativeGpuTextureHandle texture,
        NativeGpuLinearRegion readback, NativeGpuTextureAspect aspect)
    {
        ulong rowPitch = aspect == NativeGpuTextureAspect.Stencil ? 8u : 32u;
        ulong size = rowPitch * 8;
        commands.Barrier(GpuStage.DepthStencil, GpuAccess.DepthStencilRead | GpuAccess.DepthStencilWrite, GpuStage.Copy, GpuAccess.CopyRead);
        commands.CopyTextureToMemory(texture, new(readback, 0, size), new(0, aspect, 0, 1, default, new(8, 8, 1), rowPitch, size));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
    }
}
