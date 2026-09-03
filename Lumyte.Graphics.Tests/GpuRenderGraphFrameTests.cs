using Lumyte.Graphics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Tests;

public sealed class GpuRenderGraphFrameTests
{
    [Fact]
    public void ContributorsBuildInExplicitDeterministicOrder()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        builder.AddContributor("post", 0, static (context, _) =>
        {
            GpuRenderGraphResource color = context.GetResource("main-color");
            context.AddPass("composite", color, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
                .Read(color, GpuStage.PixelShader);
        }, order: 20);
        builder.AddContributor("scene", 0, static (context, _) =>
        {
            GpuRenderGraphResource color = context.ImportTexture(
                "color",
                new GpuTextureHandle(1),
                ColorDescription());
            context.PublishResource("main-color", color);
            context.AddPass("draw", color, static (_, _) => { }).Write(color, GpuStage.ColorOutput);
        }, order: 10);

        GpuRenderGraphPlan plan = builder.Compile();

        Assert.Collection(
            plan.Passes,
            pass => Assert.Equal("scene::draw", pass.Name),
            pass => Assert.Equal("post::composite", pass.Name));
        Assert.Equal("scene::color", Assert.Single(plan.Resources).Name);
    }

    [Fact]
    public void ContributorNamespacesKeepViewsIndependent()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        builder.AddContributor("camera/main", 0, static (context, _) =>
        {
            GpuRenderGraphResource color = context.CreateTexture("color", ColorDescription());
            context.AddPass("draw", color, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
                .Write(color, GpuStage.ColorOutput);
        });
        builder.AddContributor("camera/reflection", 0, static (context, _) =>
        {
            GpuRenderGraphResource color = context.CreateTexture("color", ColorDescription());
            context.AddPass("draw", color, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
                .Write(color, GpuStage.ColorOutput);
        });

        GpuRenderGraphPlan plan = builder.Compile();

        Assert.Equal(
            ["camera/main::color", "camera/reflection::color"],
            plan.Resources.Select(resource => resource.Name));
        Assert.Equal(
            ["camera/main::draw", "camera/reflection::draw"],
            plan.Passes.Select(pass => pass.Name));
    }

    [Fact]
    public void EqualOrderContributorsUseOrdinalNameOrder()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        builder.AddContributor("z-system", 0, static (context, _) =>
        {
            GpuRenderGraphResource resource = context.CreateTexture("color", ColorDescription());
            context.AddPass("draw", resource, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
                .Write(resource, GpuStage.ColorOutput);
        });
        builder.AddContributor("a-system", 0, static (context, _) =>
        {
            GpuRenderGraphResource resource = context.CreateTexture("color", ColorDescription());
            context.AddPass("draw", resource, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
                .Write(resource, GpuStage.ColorOutput);
        });

        GpuRenderGraphPlan plan = builder.Compile();

        Assert.Equal(
            ["a-system::draw", "z-system::draw"],
            plan.Passes.Select(pass => pass.Name));
    }

    [Fact]
    public void DuplicateContributorAndSharedNamesAreRejected()
    {
        var duplicateContributor = new GpuRenderGraphFrameBuilder()
            .AddContributor("scene", 0, static (_, _) => { });

        Assert.Throws<ArgumentException>(() =>
            duplicateContributor.AddContributor("scene", 0, static (_, _) => { }));

        var duplicateShared = new GpuRenderGraphFrameBuilder()
            .AddContributor("first", 0, static (context, _) =>
            {
                GpuRenderGraphResource resource = context.CreateTexture("color", ColorDescription());
                context.PublishResource("color", resource);
            })
            .AddContributor("second", 0, static (context, _) =>
            {
                GpuRenderGraphResource resource = context.CreateTexture("color", ColorDescription());
                context.PublishResource("color", resource);
            });

        Assert.Throws<ArgumentException>(duplicateShared.BuildGraph);
    }

    [Fact]
    public void CacheRebindsCurrentImportsAndCallbacks()
    {
        var cache = new GpuRenderGraphPlanCache();
        var recorded = new List<string>();
        GpuRenderGraphPlan first = CreateImportedGraph(
            new GpuTextureHandle(11),
            () => recorded.Add("first")).Compile(cache);
        GpuRenderGraphPlan second = CreateImportedGraph(
            new GpuTextureHandle(22),
            () => recorded.Add("second")).Compile(cache);

        second.Record(new RecordingQueue());

        Assert.Equal(1, cache.MissCount);
        Assert.Equal(1, cache.HitCount);
        Assert.Equal((ulong)11, Assert.Single(first.Resources).Texture.Value);
        Assert.Equal((ulong)22, Assert.Single(second.Resources).Texture.Value);
        Assert.Equal(["second"], recorded);
    }

    [Fact]
    public void StatefulPassReceivesExplicitStateThroughCacheHit()
    {
        var cache = new GpuRenderGraphPlanCache();
        var firstRecorded = new List<ulong>();
        var secondRecorded = new List<ulong>();
        CreateStatefulImportedGraph(new(31), firstRecorded).Compile(cache);
        GpuRenderGraphPlan second = CreateStatefulImportedGraph(new(47), secondRecorded).Compile(cache);

        second.Record(new RecordingQueue());

        Assert.Empty(firstRecorded);
        Assert.Equal([47ul], secondRecorded);
        Assert.Equal(1, cache.HitCount);
    }

    [Fact]
    public void StatefulPassCannotResolveAnUndeclaredResource()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource declared = graph.ImportTexture(
            "declared",
            new GpuTextureHandle(1),
            ColorDescription());
        GpuRenderGraphResource undeclared = graph.ImportTexture(
            "undeclared",
            new GpuTextureHandle(2),
            ColorDescription());
        graph.AddPass(
                "draw",
                undeclared,
                static (context, state) => _ = context.GetTexture(state),
                GpuRenderGraphPassFlags.NeverCull)
            .Read(declared, GpuStage.PixelShader);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Throws<InvalidOperationException>(() => plan.Record(new RecordingQueue()));
    }

    [Fact]
    public void StatefulContributorReceivesExplicitState()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        builder.AddContributor(
            "scene",
            new ContributorState("color", "draw"),
            static (context, state) =>
            {
                GpuRenderGraphResource resource = context.CreateTexture(
                    state.ResourceName,
                    ColorDescription());
                context.AddPass(
                        state.PassName,
                        resource,
                        static (_, _) => { },
                        GpuRenderGraphPassFlags.NeverCull)
                    .Write(resource, GpuStage.ColorOutput);
            });

        GpuRenderGraphPlan plan = builder.Compile();

        Assert.Equal("scene::color", Assert.Single(plan.Resources).Name);
        Assert.Equal("scene::draw", Assert.Single(plan.Passes).Name);
    }

    [Fact]
    public void CacheReusesSchedulingAndTransientPlans()
    {
        var cache = new GpuRenderGraphPlanCache();
        GpuRenderGraphPlan first = CreateTransientGraph(64, GpuFormat.Rgba8Unorm).Compile(cache);
        GpuRenderGraphPlan second = CreateTransientGraph(64, GpuFormat.Rgba8Unorm).Compile(cache);

        Assert.Equal(1, cache.HitCount);
        Assert.Equal(
            first.Passes.Select(pass => pass.Name),
            second.Passes.Select(pass => pass.Name));
        Assert.Equal(
            first.Barriers.Select(barrier => (barrier.Before, barrier.After, barrier.Hazards)),
            second.Barriers.Select(barrier => (barrier.Before, barrier.After, barrier.Hazards)));
        Assert.Equal(
            first.TransientResources.Select(resource => (resource.Lifetime, resource.ReuseSlot)),
            second.TransientResources.Select(resource => (resource.Lifetime, resource.ReuseSlot)));
    }

    [Fact]
    public void ResolutionFormatAndEnabledPassChangeCacheStructure()
    {
        var cache = new GpuRenderGraphPlanCache();
        CreateTransientGraph(64, GpuFormat.Rgba8Unorm).Compile(cache);
        CreateTransientGraph(128, GpuFormat.Rgba8Unorm).Compile(cache);
        CreateTransientGraph(128, GpuFormat.Bgra8Unorm).Compile(cache);
        CreateFrameWithOptionalPass(enabled: false).Compile(cache);
        CreateFrameWithOptionalPass(enabled: true).Compile(cache);

        Assert.Equal(5, cache.MissCount);
        Assert.Equal(0, cache.HitCount);
        Assert.Equal(5, cache.Count);
    }

    [Fact]
    public void CacheEvictsOldestStructureAtCapacity()
    {
        var cache = new GpuRenderGraphPlanCache(maximumEntries: 2);
        CreateTransientGraph(32, GpuFormat.Rgba8Unorm).Compile(cache);
        CreateTransientGraph(64, GpuFormat.Rgba8Unorm).Compile(cache);
        CreateTransientGraph(128, GpuFormat.Rgba8Unorm).Compile(cache);

        CreateTransientGraph(32, GpuFormat.Rgba8Unorm).Compile(cache);

        Assert.Equal(2, cache.Count);
        Assert.Equal(4, cache.MissCount);
        Assert.Equal(0, cache.HitCount);
    }

    private static GpuRenderGraph CreateImportedGraph(
        GpuTextureHandle texture,
        Action record)
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource resource = graph.ImportTexture("color", texture, ColorDescription());
        graph.AddPass(
                "draw",
                record,
                static (_, state) => state(),
                GpuRenderGraphPassFlags.NeverCull)
            .Write(resource, GpuStage.ColorOutput);
        return graph;
    }

    private static GpuRenderGraph CreateStatefulImportedGraph(
        GpuTextureHandle texture,
        List<ulong> recorded)
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource resource = graph.ImportTexture("color", texture, ColorDescription());
        graph.AddPass(
                "draw",
                new PassState(resource, recorded),
                static (context, state) => state.Recorded.Add(context.GetTexture(state.Resource).Value),
                GpuRenderGraphPassFlags.NeverCull)
            .Write(resource, GpuStage.ColorOutput);
        return graph;
    }

    private static GpuRenderGraph CreateTransientGraph(uint width, GpuFormat format)
    {
        var graph = new GpuRenderGraph();
        var description = new GpuTextureDescription(
            width,
            64,
            format,
            GpuTextureUsage.ColorAttachment);
        GpuRenderGraphResource first = graph.CreateTexture("first", description);
        GpuRenderGraphResource second = graph.CreateTexture("second", description);
        graph.AddPass("first", first, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(first, GpuStage.ColorOutput);
        graph.AddPass("second", second, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(second, GpuStage.ColorOutput);
        return graph;
    }

    private static GpuRenderGraph CreateFrameWithOptionalPass(bool enabled)
    {
        var builder = new GpuRenderGraphFrameBuilder();
        builder.AddContributor("scene", 0, static (context, _) =>
        {
            GpuRenderGraphResource color = context.CreateTexture("color", ColorDescription());
            context.AddPass("draw", color, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
                .Write(color, GpuStage.ColorOutput);
        });
        builder.AddContributor("debug", 0, static (context, _) =>
        {
            GpuRenderGraphResource color = context.CreateTexture("color", ColorDescription());
            context.AddPass("draw", color, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
                .Write(color, GpuStage.ColorOutput);
        }, enabled: enabled);
        return builder.BuildGraph();
    }

    private static GpuTextureDescription ColorDescription() => new(
        64,
        64,
        GpuFormat.Rgba8Unorm,
        GpuTextureUsage.ColorAttachment);

    private readonly record struct PassState(
        GpuRenderGraphResource Resource,
        List<ulong> Recorded);

    private readonly record struct ContributorState(string ResourceName, string PassName);

    private sealed class RecordingQueue : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording() => new(new RecordingCommandRecorder());
        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) => throw new NotSupportedException();
        public void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue)
            => throw new NotSupportedException();
        public void Wait(GpuSemaphore semaphore, ulong value) => throw new NotSupportedException();
    }

    private sealed class RecordingCommandRecorder : IGpuCommandRecorder
    {
        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards) { }
        public void BeginRendering(IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth) =>
            throw new NotSupportedException();
        public void EndRendering() => throw new NotSupportedException();
        public void SetPipeline(GpuRasterPipelineHandle pipeline) => throw new NotSupportedException();
        public void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor) => throw new NotSupportedException();
        public void Draw(uint vertexCount, uint instanceCount) => throw new NotSupportedException();
        public void CopyMemoryToTexture(GpuMemoryAddress source, GpuTextureHandle destination, GpuTextureCopyFootprint footprint) =>
            throw new NotSupportedException();
        public void CopyTextureToMemory(GpuTextureHandle source, GpuMemoryAddress destination, GpuTextureCopyFootprint footprint) =>
            throw new NotSupportedException();
        public void SetResourceTable(GpuResourceTable table) => throw new NotSupportedException();
        public void SetRootData(ReadOnlySpan<byte> data) => throw new NotSupportedException();
        public void End() { }
    }
}
