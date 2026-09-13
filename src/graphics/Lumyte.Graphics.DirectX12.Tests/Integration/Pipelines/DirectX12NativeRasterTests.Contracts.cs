using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeRasterTests
{
    [Fact]
    public void AttachmentClearValuesAreCopiedAtBeginRendering()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        NativeGpuColorAttachment[] colors = [new(color.RenderView, NativeGpuLoadOp.Clear, ClearColor: new(1, 0, 0, 1))];
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering(colors);
        colors[0] = new(color.RenderView, NativeGpuLoadOp.Clear, ClearColor: new(0, 1, 0, 1));
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, color);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(readback, 8, 8));
    }

    [Fact]
    public void DiscardedAttachmentCanBeClearedByTheNextPass()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Discard, NativeGpuStoreOp.Discard)]);
        commands.EndRendering();
        commands.Barrier(GpuStage.ColorOutput, GpuAccess.ColorWrite, GpuStage.ColorOutput, GpuAccess.ColorWrite);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear, ClearColor: new(0, 0, 1, 1))]);
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, color);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(readback, 8, 8));
    }

    [Fact]
    public void AnInvalidUnusedShaderDoesNotCreateANativePso()
    {
        using var gpu = new Fixture();
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(vertex: [1, 2, 3, 4]);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.SetPipeline(pipeline);

        gpu.Submit(commands);

        Assert.Equal(0, gpu.Backend.RasterPipelineVariantCount(pipeline));
    }

    [Fact]
    public void RasterPsoFailureRejectsTheWholeSubmissionBeforeExecution()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        NativeGpuLinearRegion upload = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        NativeGpuLinearRegion readback = gpu.Region(NativeGpuMemoryKind.Readback);
        Marshal.WriteInt32(upload.CpuAddress, 11);
        Marshal.WriteInt32(readback.CpuAddress, 97);
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(vertex: [1, 2, 3, 4]);
        using NativeGpuCommandBuffer first = gpu.Commands();
        using NativeGpuCommandBuffer second = gpu.Commands();
        using NativeGpuSemaphore semaphore = gpu.Backend.MainQueue.CreateSemaphore(0);
        first.CopyMemory(new(upload, 0, 4), new(readback, 0, 4));
        second.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        second.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
        second.SetPipeline(pipeline);
        second.Draw([], 3);
        second.EndRendering();

        NativeGpuException error = Assert.Throws<NativeGpuException>(() => gpu.Backend.MainQueue.Submit([first, second], semaphore, 1));
        using NativeGpuCommandBuffer drain = gpu.Commands();
        gpu.Submit(drain);

        Assert.Contains("CreateGraphicsPipelineState", error.Message);
        Assert.Equal(97, Marshal.ReadInt32(readback.CpuAddress));
        Assert.Equal(0, gpu.Backend.RasterPipelineVariantCount(pipeline));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlyAndAbsentAspectsCannotSilentlyDiscardOperations(bool readOnly)
    {
        using var gpu = new Fixture();
        Target depth = gpu.Texture(GpuFormat.D32Float,
            readOnly ? NativeGpuRenderViewFlags.DepthReadOnly : NativeGpuRenderViewFlags.None);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        NativeGpuDepthStencilAttachment attachment = readOnly
            ? new(depth.RenderView, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store)
            : new(depth.RenderView, NativeGpuLoadOp.Load, NativeGpuStoreOp.Store, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store);

        ArgumentException error = Assert.Throws<ArgumentException>(() => commands.BeginRendering([], attachment));

        Assert.Equal("attachment", error.ParamName);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("barrier")]
    [InlineData("dispatch")]
    [InlineData("nested")]
    public void RenderingRejectsOperationsFromOutsideItsScope(string operation)
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        NativeGpuLinearRegion memory = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.BeginRendering([new(color.RenderView)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        {
            switch (operation)
            {
                case "copy": commands.CopyMemory(new(memory, 0, 4), new(memory, 4, 4)); break;
                case "barrier": commands.Barrier(GpuStage.All, GpuAccess.None, GpuStage.All, GpuAccess.None); break;
                case "dispatch": commands.Dispatch([], 1); break;
                case "nested": commands.BeginRendering([new(color.RenderView)]); break;
            }
        });

        Assert.Contains("rendering", error.Message);
        commands.EndRendering();
    }

    [Fact]
    public void RenderingMustEndBeforeSubmission()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        using NativeGpuCommandBuffer commands = gpu.Commands();
        using NativeGpuSemaphore semaphore = gpu.Backend.MainQueue.CreateSemaphore(0);
        commands.BeginRendering([new(color.RenderView)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => gpu.Backend.MainQueue.Submit([commands], semaphore, 1));

        Assert.Contains("End rendering", error.Message);
    }

    [Fact]
    public void AForeignRasterPipelineCannotBeSelected()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        NativeGpuRasterPipelineHandle pipeline = first.Pipeline();
        using NativeGpuCommandBuffer commands = second.Commands();

        ArgumentException error = Assert.Throws<ArgumentException>(() => commands.SetPipeline(pipeline));

        Assert.Equal("pipeline", error.ParamName);
    }
}
