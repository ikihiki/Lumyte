using System.Numerics;
using System.Runtime.InteropServices;

using Lumyte.Graphics;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.RenderGraph.Common;

namespace Lumyte.Graphics.RenderGraph.Common.Tests;

public sealed class DrawRenderPassTests
{
    [Fact]
    public void PassDeclaresMaterialAndTargetAccesses()
    {
        var graph = new GpuRenderGraph();
        DrawData draw = DrawData(
            resources: new(1, 0),
            sampledTexture: new(
                new(41),
                new(16, 16, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled),
                GpuStage.VertexShader));

        DrawRenderPassResources resources = graph.AddDraw(
            "opaque",
            draw,
            Target(),
            markOutput: true);
        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Equal("opaque", Assert.Single(plan.Passes).Name);
        GpuRenderGraphBarrierPlan barrier = Assert.Single(plan.Barriers);
        Assert.Equal(GpuStage.VertexShader | GpuStage.ColorOutput, barrier.After);
        Assert.Equal(GpuBarrierHazards.Descriptors, barrier.Hazards);
        Assert.Equal(2, barrier.ResourceCount);
        Assert.Single(resources.SampledTextures);
    }

    [Fact]
    public void CachedFrameRecordsCurrentMaterialAndProceduralDraw()
    {
        var cache = new GpuRenderGraphPlanCache();
        CreatePlan(cache, pipelineId: 13, targetId: 14, vertexCount: 3);
        GpuRenderGraphPlan current = CreatePlan(cache, pipelineId: 23, targetId: 24, vertexCount: 36);
        var recorder = new RecordingCommandRecorder();

        current.Record(new RecordingQueue(recorder));

        Assert.Equal(1, cache.HitCount);
        Assert.Equal([
            "barrier:None->ColorOutput",
            "begin:24",
            "pipeline:23",
            "resources",
            "root:128",
            "viewport:64x48",
            "draw:36x1",
            "end-rendering",
        ], recorder.Events);
        Assert.Equal(Matrix4x4.CreateTranslation(1, 2, 3), recorder.World);
        Assert.Equal(Matrix4x4.CreateScale(2), recorder.ViewProjection);
    }

    [Fact]
    public void FrameBuilderUsesAPublishedTarget()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        builder.AddContributor(
            "target",
            Target(),
            static (context, target) => context.PublishTexture(
                "main-color",
                context.ImportTexture("color", target.View.Texture, target.Description)),
            order: 0);
        builder.AddContributor(
            "scene",
            (Draw: DrawData(), Target: Target()),
            static (context, state) => context.AddDraw(
                "main-draw",
                state.Draw,
                state.Target,
                context.GetTexture("main-color")),
            order: 1);

        GpuRenderGraphPlan plan = builder.Compile();

        Assert.Equal("scene::main-draw", Assert.Single(plan.Passes).Name);
        Assert.Equal(1, plan.TextureCount);
    }

    [Fact]
    public void GeometryRejectsAnEmptyDraw()
    {
        var geometry = new ProceduralGeometry(0);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => geometry.Validate());

        Assert.Equal("VertexCount", exception.ParamName);
    }

    [Fact]
    public void MaterialRequiresEveryTextureSlotToBeDeclared()
    {
        var resources = new GpuResourceTable(1, 0);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new DrawMaterial(new(1), resources));

        Assert.Equal("sampledTextures", exception.ParamName);
    }

    private static GpuRenderGraphPlan CreatePlan(
        GpuRenderGraphPlanCache cache,
        ulong pipelineId,
        ulong targetId,
        uint vertexCount)
    {
        var graph = new GpuRenderGraph();
        graph.AddDraw(
            "draw",
            DrawData(pipelineId, vertexCount: vertexCount, resources: new(0, 1)),
            Target(targetId));
        return graph.Compile(cache);
    }

    private static DrawData DrawData(
        ulong pipelineId = 3,
        uint vertexCount = 6,
        GpuResourceTable? resources = null,
        DrawSampledTexture? sampledTexture = null)
    {
        DrawSampledTexture[] textures = sampledTexture is { } texture ? [texture] : [];
        return new(
            new(new(pipelineId), resources, textures),
            new(vertexCount),
            new(Matrix4x4.CreateTranslation(1, 2, 3), Matrix4x4.CreateScale(2)));
    }

    private static DrawRenderTarget Target(ulong textureId = 4)
    {
        var description = new GpuTextureDescription(
            64,
            48,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment);
        var texture = new GpuTextureHandle(textureId);
        return new(
            new(new(textureId + 100), texture, new(description.Format)),
            description);
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
        public Matrix4x4 World { get; private set; }
        public Matrix4x4 ViewProjection { get; private set; }

        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards) =>
            Events.Add($"barrier:{before}->{after}");
        public void BeginRendering(IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth) =>
            Events.Add($"begin:{Assert.Single(colors).View.Texture.Value}");
        public void EndRendering() => Events.Add("end-rendering");
        public void SetPipeline(GpuRasterPipelineHandle pipeline) => Events.Add($"pipeline:{pipeline.Value}");
        public void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor) =>
            Events.Add($"viewport:{viewport.Width}x{viewport.Height}");
        public void Draw(uint vertexCount, uint instanceCount) => Events.Add($"draw:{vertexCount}x{instanceCount}");
        public void CopyMemoryToTexture(GpuMemoryAddress source, GpuTextureHandle destination, GpuTextureCopyFootprint footprint)
            => throw new NotSupportedException();
        public void CopyTextureToMemory(GpuTextureHandle source, GpuMemoryAddress destination, GpuTextureCopyFootprint footprint)
            => throw new NotSupportedException();
        public void SetResourceTable(GpuResourceTable table) => Events.Add("resources");
        public void SetRootData(ReadOnlySpan<byte> data)
        {
            Events.Add($"root:{data.Length}");
            World = MemoryMarshal.Read<Matrix4x4>(data);
            ViewProjection = MemoryMarshal.Read<Matrix4x4>(data[64..]);
        }
        public void End() => Events.Add("end");
    }
}
