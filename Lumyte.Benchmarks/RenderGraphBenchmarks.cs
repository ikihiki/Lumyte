using BenchmarkDotNet.Attributes;
using Lumyte.Graphics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Benchmarks;

[MemoryDiagnoser]
public class RenderGraphBenchmarks
{
    private const int PassCount = 8;
    private GpuRenderGraph cachedGraph = null!;
    private GpuRenderGraphPlan recordPlan = null!;
    private GpuRenderGraphPlan executePlan = null!;
    private GpuRenderGraphPlanCache cache = null!;
    private NullQueue queue = null!;
    private CpuBackend backend = null!;
    private ulong missSequence;

    [GlobalSetup]
    public void Setup()
    {
        cachedGraph = CreateStatefulGraph(PassCount, 64);
        cache = new();
        cachedGraph.Compile(cache);
        recordPlan = CreateStatefulImportedGraph(PassCount).Compile();
        queue = new();
        executePlan = CreateStatefulGraph(PassCount, 64).Compile();
        backend = new();
    }

    [Benchmark(Baseline = true)]
    public GpuRenderGraphPlan SinglePassCompile()
        => CreateStatefulGraph(1, 64).Compile();

    [Benchmark]
    public GpuRenderGraphPlan SmallMultiPassCompile()
        => CreateStatefulGraph(PassCount, 64).Compile();

    [Benchmark]
    public GpuRenderGraphPlan CacheHit()
        => cachedGraph.Compile(cache);

    [Benchmark]
    public GpuRenderGraphPlan CacheMiss()
    {
        ulong size = 128 + missSequence++;
        return CreateStatefulGraph(PassCount, size).Compile(cache);
    }

    [Benchmark]
    public GpuRenderGraphPlan RegisterMultipleContributors()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        for (int index = 0; index < PassCount; index++)
        {
            builder.AddContributor(
                $"system-{index}",
                index,
                static (context, contributorIndex) =>
                {
                    GpuRenderGraphBuffer resource = context.CreateBuffer(
                        "buffer",
                        new(64, GpuBufferUsage.ShaderData));
                    context.AddPass(
                            "pass",
                            resource,
                            static (pass, state) => _ = pass.GetBuffer(state),
                            GpuRenderGraphPassFlags.NeverCull)
                        .Write(resource, GpuStage.ComputeShader);
                    GC.KeepAlive(contributorIndex);
                },
                order: PassCount - index);
        }
        return builder.Compile();
    }

    [Benchmark]
    public GpuCommandBuffer RecordImportedPlan()
        => recordPlan.Record(queue);

    [Benchmark]
    public int ExecuteTransientPlan()
    {
        using GpuRenderGraphExecution execution = executePlan.Execute(backend);
        return backend.DestroyedBufferCount;
    }

    private static GpuRenderGraph CreateStatefulGraph(int passCount, ulong size)
    {
        var graph = new GpuRenderGraph();
        for (int index = 0; index < passCount; index++)
        {
            GpuRenderGraphBuffer resource = graph.CreateBuffer(
                $"buffer-{index}",
                new(size + (ulong)index, GpuBufferUsage.ShaderData));
            graph.AddPass(
                    $"pass-{index}",
                    resource,
                    static (context, state) => _ = context.GetBuffer(state),
                    GpuRenderGraphPassFlags.NeverCull)
                .Write(resource, GpuStage.ComputeShader);
        }
        return graph;
    }

    private static GpuRenderGraph CreateStatefulImportedGraph(int passCount)
    {
        var graph = new GpuRenderGraph();
        for (int index = 0; index < passCount; index++)
        {
            var description = new GpuBufferDescription(64, GpuBufferUsage.ShaderData);
            GpuRenderGraphBuffer resource = graph.ImportBuffer(
                $"buffer-{index}",
                new GpuBufferHandle((ulong)index + 1, 64),
                description);
            graph.AddPass(
                    $"pass-{index}",
                    resource,
                    static (context, state) => _ = context.GetBuffer(state),
                    GpuRenderGraphPassFlags.NeverCull)
                .Read(resource, GpuStage.ComputeShader);
        }
        return graph;
    }

    private sealed class NullQueue : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording()
            => GpuBackendCommands.CreateCommandBuffer(new NullRecorder());
        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) => throw new NotSupportedException();
        public void Submit(
            ReadOnlySpan<GpuCommandBuffer> commandBuffers,
            GpuSemaphore signalSemaphore,
            ulong signalValue) => throw new NotSupportedException();
        public void Wait(GpuSemaphore semaphore, ulong value) => throw new NotSupportedException();
    }

    private sealed class NullRecorder : IGpuCommandRecorder
    {
        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards) { }
        public void BeginRendering(
            IReadOnlyList<GpuColorAttachment> colors,
            GpuDepthStencilAttachment? depth) { }
        public void EndRendering() { }
        public void SetPipeline(GpuRasterPipelineHandle pipeline) { }
        public void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor) { }
        public void Draw(uint vertexCount, uint instanceCount) { }
        public void CopyMemoryToTexture(
            GpuMemoryAddress source,
            GpuTextureHandle destination,
            GpuTextureCopyFootprint footprint) { }
        public void CopyTextureToMemory(
            GpuTextureHandle source,
            GpuMemoryAddress destination,
            GpuTextureCopyFootprint footprint) { }
        public void SetResourceTable(GpuResourceTable table) { }
        public void SetRootData(ReadOnlySpan<byte> data) { }
        public void End() { }
    }

    private sealed class CpuBackend : IGpuBackend
    {
        private readonly ImmediateQueue queue = new();
        private ulong nextBuffer;

        public GpuBackendCapabilities Capabilities => GpuBackendCapabilities.DeviceOwnedResources;
        public IGpuQueue MainQueue => queue;
        public int DestroyedBufferCount { get; private set; }

        public GpuBufferHandle CreateBuffer(GpuBufferDescription description)
            => new(++nextBuffer, description.Size);

        public void DestroyBuffer(GpuBufferHandle buffer) => DestroyedBufferCount++;
    }

    private sealed class ImmediateQueue : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording()
            => GpuBackendCommands.CreateCommandBuffer(new NullRecorder());
        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) => new ImmediateSemaphore();

        public void Submit(
            ReadOnlySpan<GpuCommandBuffer> commandBuffers,
            GpuSemaphore signalSemaphore,
            ulong signalValue)
        {
            foreach (GpuCommandBuffer commandBuffer in commandBuffers)
            {
                GpuBackendCommands.Finish(commandBuffer);
            }
        }

        public void Wait(GpuSemaphore semaphore, ulong value) { }
    }

    private sealed class ImmediateSemaphore : GpuSemaphore
    {
        public override void Dispose() { }
    }
}
