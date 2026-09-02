using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuCommandBufferTests
{
    [Fact]
    public void CommandsRecordImmediatelyWithoutResourceBarriers()
    {
        var recorder = new RecordingCommandRecorder();
        var texture = new GpuTextureHandle(1);
        var view = new GpuTextureView(new(1), texture, new(GpuFormat.Rgba8Unorm));
        var attachment = new GpuColorAttachment(
            view,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(1, 0, 0, 1));
        var commands = new GpuCommandBuffer(recorder)
            .Barrier(GpuStage.None, GpuStage.ColorOutput)
            .BeginRendering([attachment])
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToMemory(texture, new(2, 16, 4), new(1, 1, 4, 4));

        Assert.Equal(["barrier:None>ColorOutput:None", "begin", "end", "barrier:ColorOutput>Copy:None", "copy:2+16:4"], recorder.Events);
    }

    [Fact]
    public void CopyCannotBeRecordedInsideRendering()
    {
        var texture = new GpuTextureHandle(1);
        var view = new GpuTextureView(new(1), texture, new(GpuFormat.Rgba8Unorm));
        var attachment = new GpuColorAttachment(
            view,
            GpuAttachmentLoadOperation.Load,
            GpuAttachmentStoreOperation.Store);
        var commands = new GpuCommandBuffer(new RecordingCommandRecorder()).BeginRendering([attachment]);

        Assert.Throws<InvalidOperationException>(
            () => commands.CopyTextureToMemory(texture, new(2, 0, 4), new(1, 1, 4, 4)));
    }

    [Fact]
    public void SubmittedCommandBufferCannotBeRecordedOrSubmittedAgain()
    {
        var recorder = new RecordingCommandRecorder();
        var commands = new GpuCommandBuffer(recorder).Barrier(GpuStage.ComputeShader, GpuStage.ComputeShader);

        commands.Finish();

        Assert.Throws<InvalidOperationException>(() => commands.Barrier(GpuStage.Copy, GpuStage.Copy));
        Assert.Throws<InvalidOperationException>(() => commands.Finish());
        Assert.Equal(1, recorder.EndCount);
    }

    [Fact]
    public void RasterStateAndDrawRecordInsideRendering()
    {
        var recorder = new RecordingCommandRecorder();
        var texture = new GpuTextureHandle(1);
        var attachment = new GpuColorAttachment(
            new(new(1), texture, new(GpuFormat.Rgba8Unorm)),
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store);

        new GpuCommandBuffer(recorder)
            .BeginRendering([attachment])
            .SetPipeline(new(7))
            .SetViewportAndScissor(new(0, 0, 64, 64), new(0, 0, 64, 64))
            .Draw(3)
            .EndRendering();

        Assert.Equal(["begin", "pipeline", "viewport", "draw:3:1", "end"], recorder.Events);
    }

    [Fact]
    public void UploadAndSampledBindingRecordInTheirRequiredScopes()
    {
        var recorder = new RecordingCommandRecorder();
        var texture = new GpuTextureHandle(5);
        var attachment = new GpuColorAttachment(
            new(new(1), new GpuTextureHandle(6), new(GpuFormat.Rgba8Unorm)),
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store);
        new GpuCommandBuffer(recorder)
            .CopyMemoryToTexture(new(2, 0, 16), texture, new(2, 2, 4, 8))
            .Barrier(GpuStage.Copy, GpuStage.PixelShader)
            .BeginRendering([attachment])
            .SetPipeline(new(7))
            .SetResourceTable(new(1, 1))
            .SetRootData(BitConverter.GetBytes(0u))
            .Draw(6)
            .EndRendering();

        Assert.Equal(["upload:2+0:16", "barrier:Copy>PixelShader:None", "begin", "pipeline", "resources", "root:4", "draw:6:1", "end"], recorder.Events);
    }

    private sealed class RecordingCommandRecorder : IGpuCommandRecorder
    {
        public List<string> Events { get; } = [];
        public int EndCount { get; private set; }
        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards) => Events.Add($"barrier:{before}>{after}:{hazards}");
        public void BeginRendering(IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth) => Events.Add("begin");
        public void EndRendering() => Events.Add("end");
        public void SetPipeline(GpuRasterPipelineHandle pipeline) => Events.Add("pipeline");
        public void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor) => Events.Add("viewport");
        public void Draw(uint vertexCount, uint instanceCount) => Events.Add($"draw:{vertexCount}:{instanceCount}");
        public void CopyMemoryToTexture(GpuMemoryAddress source, GpuTextureHandle destination, GpuTextureCopyFootprint footprint) => Events.Add($"upload:{source.AllocationId}+{source.Offset}:{footprint.RequiredBytes}");
        public void CopyTextureToMemory(GpuTextureHandle source, GpuMemoryAddress destination, GpuTextureCopyFootprint footprint) => Events.Add($"copy:{destination.AllocationId}+{destination.Offset}:{footprint.RequiredBytes}");
        public void SetResourceTable(GpuResourceTable table) => Events.Add("resources");
        public void SetRootData(ReadOnlySpan<byte> data) => Events.Add($"root:{data.Length}");
        public void End() => EndCount++;
    }
}
