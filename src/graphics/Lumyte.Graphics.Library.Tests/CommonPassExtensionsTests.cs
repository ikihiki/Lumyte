using Lumyte.Graphics;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Library;

namespace Lumyte.Graphics.Library.Tests;

public sealed class CommonPassExtensionsTests
{
    [Fact]
    public void ClearRecordsAColorAttachmentClear()
    {
        var graph = new GpuRenderGraph();
        DrawRenderTarget target = Target(loadOperation: GpuAttachmentLoadOperation.Clear);
        graph.AddClear("clear", target);
        var recorder = new RecordingCommandRecorder();

        graph.Compile().Record(new RecordingQueue(recorder));

        Assert.Equal(GpuAttachmentLoadOperation.Clear, recorder.LoadOperation);
        Assert.Equal(target.ClearColor, recorder.ClearColor);
        Assert.Equal(["barrier", "begin", "end-rendering"], recorder.Events);
    }

    [Fact]
    public void FullscreenRecordsAThreeVertexDraw()
    {
        var graph = new GpuRenderGraph();
        var recorder = new RecordingCommandRecorder();

        graph.AddFullscreen("fullscreen", new(new(7)), Target());
        graph.Compile().Record(new RecordingQueue(recorder));

        Assert.Equal((3u, 1u), recorder.RecordedDraw);
    }

    [Fact]
    public void ComputeBindsDeclaredBuffersAndDispatches()
    {
        var graph = new GpuRenderGraph();
        var description = new GpuBufferDescription(64, GpuBufferUsage.ShaderData);
        GpuRenderGraphBuffer input = graph.ImportBuffer("input", new(11, 64), description);
        GpuRenderGraphBuffer output = graph.ImportBuffer("output", new(12, 64), description);
        var compute = new ComputeData(
            new(9),
            new(2, 3, 4),
            [
                new(0, input, GpuRenderGraphAccess.Read),
                new(1, output, GpuRenderGraphAccess.Write),
            ]);
        var recorder = new RecordingCommandRecorder();

        graph.AddCompute("compute", compute);
        GpuRenderGraphPlan plan = graph.Compile();
        plan.Record(new RecordingQueue(recorder));

        Assert.Equal(new GpuComputePipelineHandle(9), recorder.ComputePipeline);
        Assert.Equal([(0u, 11ul), (1u, 12ul)], recorder.ComputeBuffers);
        Assert.Equal((2u, 3u, 4u), recorder.RecordedDispatch);
        Assert.Equal(2, Assert.Single(plan.Barriers).ResourceCount);
    }

    [Fact]
    public void BlitRequiresOneSampledTexture()
    {
        var graph = new GpuRenderGraph();
        var material = new DrawMaterial(new(1));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => graph.AddBlit("blit", material, Target()));

        Assert.Equal("material", exception.ParamName);
    }

    [Fact]
    public void CompositeRequiresTwoSampledTextures()
    {
        var graph = new GpuRenderGraph();
        DrawMaterial material = MaterialWithOneTexture();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => graph.AddComposite("composite", material, Target()));

        Assert.Equal("material", exception.ParamName);
    }

    private static DrawMaterial MaterialWithOneTexture()
    {
        var resources = new GpuResourceTable(1, 0);
        resources.SetTexture(0, new(31));
        var description = new GpuTextureDescription(2, 2, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled);
        return new(new(1), resources, [new(new(21), description)]);
    }

    private static DrawRenderTarget Target(
        GpuAttachmentLoadOperation loadOperation = GpuAttachmentLoadOperation.Load)
    {
        var description = new GpuTextureDescription(
            64,
            48,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment);
        var texture = new GpuTextureHandle(4);
        return new(
            new(new(104), texture, new(description.Format)),
            description,
            loadOperation,
            GpuAttachmentStoreOperation.Store,
            new(0.1f, 0.2f, 0.3f, 1));
    }

    private sealed class RecordingQueue(RecordingCommandRecorder recorder) : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording() => GpuBackendCommands.CreateCommandBuffer(recorder);
        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) => throw new NotSupportedException();
        public void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue)
            => throw new NotSupportedException();
        public void Wait(GpuSemaphore semaphore, ulong value) => throw new NotSupportedException();
    }

    private sealed class RecordingCommandRecorder : IGpuCommandRecorder
    {
        public List<string> Events { get; } = [];
        public GpuAttachmentLoadOperation LoadOperation { get; private set; }
        public GpuClearColor ClearColor { get; private set; }
        public (uint VertexCount, uint InstanceCount) RecordedDraw { get; private set; }
        public GpuComputePipelineHandle ComputePipeline { get; private set; }
        public List<(uint Slot, ulong Buffer)> ComputeBuffers { get; } = [];
        public (uint X, uint Y, uint Z) RecordedDispatch { get; private set; }

        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards) => Events.Add("barrier");
        public void BeginRendering(IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth)
        {
            GpuColorAttachment attachment = Assert.Single(colors);
            LoadOperation = attachment.LoadOperation;
            ClearColor = attachment.ClearColor;
            Events.Add("begin");
        }
        public void EndRendering() => Events.Add("end-rendering");
        public void SetPipeline(GpuRasterPipelineHandle pipeline) { }
        public void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor) { }
        public void Draw(uint vertexCount, uint instanceCount) => RecordedDraw = (vertexCount, instanceCount);
        public void CopyMemoryToTexture(GpuMemoryAddress source, GpuTextureHandle destination, GpuTextureCopyFootprint footprint)
            => throw new NotSupportedException();
        public void CopyTextureToMemory(GpuTextureHandle source, GpuMemoryAddress destination, GpuTextureCopyFootprint footprint)
            => throw new NotSupportedException();
        public void SetResourceTable(GpuResourceTable table) { }
        public void SetRootData(ReadOnlySpan<byte> data) { }
        public void SetComputePipeline(GpuComputePipelineHandle pipeline) => ComputePipeline = pipeline;
        public void SetComputeBuffer(uint slot, GpuBufferHandle buffer) => ComputeBuffers.Add((slot, buffer.Value));
        public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ) =>
            RecordedDispatch = (groupCountX, groupCountY, groupCountZ);
        public void End() { }
    }
}
