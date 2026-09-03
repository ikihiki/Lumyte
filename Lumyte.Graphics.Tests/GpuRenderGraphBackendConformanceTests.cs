using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public abstract class GpuRenderGraphBackendConformanceTests
{
    private const uint PixelWidth = 64;
    private const uint PixelHeight = 64;
    private const ulong PixelRowPitch = PixelWidth * 4;
    private const ulong PixelByteCount = PixelRowPitch * PixelHeight;

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void QueueCompletionCanBePolledAfterWait()
    {
        using IGpuBackend backend = CreateBackend();
        IGpuQueue queue = backend.MainQueue;
        GpuCommandBuffer commands = queue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.All);
        using GpuSemaphore completion = queue.CreateSemaphore();
        queue.Submit([commands], completion, 1);

        queue.Wait(completion, 1);

        Assert.True(queue.IsComplete(completion, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.IsComplete(completion, 2));
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void GraphCanRetireAnAsynchronousSubmission()
    {
        using IGpuBackend backend = CreateBackend();
        using var retirements = new GpuRetirementQueue(backend);
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource texture = graph.CreateTexture(
            "transient",
            new(
                PixelWidth,
                PixelHeight,
                GpuFormat.Rgba8Unorm,
                GpuTextureUsage.ColorAttachment));
        graph.AddPass("clear", texture, static (context, state) =>
        {
            GpuTextureView view = context.GetTextureView(state);
            context.Commands.BeginRendering([
                new(
                    view,
                    GpuAttachmentLoadOperation.Clear,
                    GpuAttachmentStoreOperation.Store,
                    new(0.1f, 0.2f, 0.3f, 1)),
            ]).EndRendering();
        }, GpuRenderGraphPassFlags.NeverCull).Write(texture, GpuStage.ColorOutput);

        using GpuRenderGraphExecution execution =
            graph.Compile().ExecuteAsync(backend, retirements);
        execution.WaitForCompletion();

        Assert.True(execution.IsComplete);
        Assert.Equal(0, retirements.InFlightSubmissionCount);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void CachedAsynchronousPlanExecutesWithCurrentFrameBindings()
    {
        using IGpuBackend backend = CreateBackend();
        using var retirements = new GpuRetirementQueue(backend);
        var cache = new GpuRenderGraphPlanCache();
        CreateClearGraph(new(0.1f, 0.2f, 0.3f, 1)).Compile(cache);
        GpuRenderGraphPlan plan = CreateClearGraph(new(0.7f, 0.6f, 0.5f, 1)).Compile(cache);

        using GpuRenderGraphExecution execution = plan.ExecuteAsync(backend, retirements);
        execution.WaitForCompletion();

        Assert.Equal(1, cache.HitCount);
        Assert.True(execution.IsComplete);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void GraphExecutesPassesInDependencyOrder()
    {
        using IGpuBackend backend = CreateBackend();
        using var source = TestTexture.Create(backend);
        using var output = TestTexture.Create(backend);
        var executed = new List<string>();
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource sourceResource = graph.ImportTexture("source", source.Handle);
        GpuRenderGraphResource outputResource = graph.ImportTexture("output", output.Handle);
        graph.AddPass("produce", executed, static (_, state) => state.Add("produce"))
            .Write(sourceResource, GpuStage.Copy);
        graph.AddPass("consume", executed, static (_, state) => state.Add("consume"))
            .Read(sourceResource, GpuStage.PixelShader)
            .Write(outputResource, GpuStage.ColorOutput);
        graph.MarkOutput(outputResource);

        GpuRenderGraphPlan plan = graph.Compile();
        Execute(backend, plan);

        Assert.Equal(["produce", "consume"], executed);
        Assert.Collection(
            plan.Passes,
            pass => Assert.Equal("produce", pass.Name),
            pass => Assert.Equal("consume", pass.Name));
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void GraphDoesNotExecutePassesOutsideOutputDependencies()
    {
        using IGpuBackend backend = CreateBackend();
        using var output = TestTexture.Create(backend);
        using var unused = TestTexture.Create(backend);
        var executed = new List<string>();
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource outputResource = graph.ImportTexture("output", output.Handle);
        GpuRenderGraphResource unusedResource = graph.ImportTexture("unused", unused.Handle);
        graph.AddPass("unused", executed, static (_, state) => state.Add("unused"))
            .Write(unusedResource, GpuStage.ComputeShader);
        graph.AddPass("output", executed, static (_, state) => state.Add("output"))
            .Write(outputResource, GpuStage.ColorOutput);
        graph.MarkOutput(outputResource);

        GpuRenderGraphPlan plan = graph.Compile();
        Execute(backend, plan);

        Assert.Equal(["output"], executed);
        Assert.Equal("output", Assert.Single(plan.Passes).Name);
        Assert.DoesNotContain(plan.Barriers, barrier => barrier.DestinationPass == "unused");
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void GraphPreservesReadWriteConflictOrder()
    {
        using IGpuBackend backend = CreateBackend();
        using var texture = TestTexture.Create(backend);
        var executed = new List<string>();
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource resource = graph.ImportTexture("shared", texture.Handle);
        graph.AddPass("initial-write", executed, static (_, state) => state.Add("initial-write"))
            .Write(resource, GpuStage.Copy);
        graph.AddPass("read", executed, static (_, state) => state.Add("read"), GpuRenderGraphPassFlags.NeverCull)
            .Read(resource, GpuStage.PixelShader);
        graph.AddPass("overwrite", executed, static (_, state) => state.Add("overwrite"))
            .Write(resource, GpuStage.ColorOutput);
        graph.AddPass("final-read", executed, static (_, state) => state.Add("final-read"), GpuRenderGraphPassFlags.NeverCull)
            .Read(resource, GpuStage.Copy);
        graph.MarkOutput(resource);

        GpuRenderGraphPlan plan = graph.Compile();
        Execute(backend, plan);

        Assert.Equal(["initial-write", "read", "overwrite", "final-read"], executed);
        Assert.Equal(
            ["initial-write", "read", "overwrite", "final-read"],
            plan.Passes.Select(pass => pass.Name));
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void GraphExecutesTheSharedBarrierTransitionPlan()
    {
        using IGpuBackend backend = CreateBackend();
        using var source = TestTexture.Create(backend);
        using var output = TestTexture.Create(backend);
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource sourceResource = graph.ImportTexture("source", source.Handle);
        GpuRenderGraphResource outputResource = graph.ImportTexture("output", output.Handle);
        graph.AddPass("copy", sourceResource, static (_, _) => { })
            .Write(sourceResource, GpuStage.Copy, GpuBarrierHazards.Descriptors);
        graph.AddPass("draw", outputResource, static (_, _) => { })
            .Read(sourceResource, GpuStage.PixelShader)
            .Write(outputResource, GpuStage.ColorOutput);
        graph.MarkOutput(outputResource);

        GpuRenderGraphPlan plan = graph.Compile();
        Execute(backend, plan);

        Assert.Collection(
            plan.Barriers,
            barrier => AssertBarrier(
                barrier,
                "copy",
                GpuStage.None,
                GpuStage.Copy,
                GpuBarrierHazards.Descriptors,
                sourceResource),
            barrier => AssertBarrier(
                barrier,
                "draw",
                GpuStage.Copy,
                GpuStage.PixelShader | GpuStage.ColorOutput,
                GpuBarrierHazards.Descriptors,
                sourceResource,
                outputResource));
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void TransientPixelsCanBeExportedAndImportedAcrossGraphs()
    {
        using IGpuBackend backend = CreateBackend();
        var description = new GpuTextureDescription(
            PixelWidth,
            PixelHeight,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
        var producer = new GpuRenderGraph();
        GpuRenderGraphResource transient = producer.CreateTexture("transient", description);
        GpuRenderGraphResource unused = producer.CreateTexture("unused", description);
        producer.AddPass("unused", unused, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(1, 0, 0, 1)),
                ])
                .EndRendering())
            .Write(unused, GpuStage.ColorOutput);
        producer.AddPass("clear", transient, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(0.25f, 0.5f, 0.75f, 1)),
                ])
                .EndRendering())
            .Write(transient, GpuStage.ColorOutput);
        producer.AddPass("preserve", transient, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Load,
                        GpuAttachmentStoreOperation.Store),
                ])
                .EndRendering())
            .ReadWrite(transient, GpuStage.ColorOutput);
        producer.ExportTexture(transient);

        GpuRenderGraphPlan producerPlan = producer.Compile();
        using GpuRenderGraphExecution producerExecution = producerPlan.Execute(backend);
        GpuRenderGraphExportedTexture exported = producerExecution.GetTexture(transient);
        var consumer = new GpuRenderGraph();
        GpuRenderGraphResource imported = consumer.ImportTexture("imported", exported);
        consumer.AddPass("consume", imported, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Load,
                        GpuAttachmentStoreOperation.Store),
                ])
                .EndRendering(),
                GpuRenderGraphPassFlags.NeverCull)
            .ReadWrite(imported, GpuStage.ColorOutput);
        GpuRenderGraphPlan consumerPlan = consumer.Compile();
        using GpuRenderGraphExecution consumerExecution = consumerPlan.Execute(backend);
        byte[] pixels = ReadPixels(backend, exported.Texture);

        Assert.Collection(
            producerPlan.Passes,
            pass => Assert.Equal("clear", pass.Name),
            pass => Assert.Equal("preserve", pass.Name));
        Assert.Equal("consume", Assert.Single(consumerPlan.Passes).Name);
        AssertPixelNear(pixels, PixelWidth / 2, PixelHeight / 2, 64, 128, 191, 255);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void NonOverlappingTransientTexturesReuseAPlannedSlot()
    {
        using IGpuBackend backend = CreateBackend();
        var description = new GpuTextureDescription(
            PixelWidth,
            PixelHeight,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource first = graph.CreateTexture("first", description);
        GpuRenderGraphResource second = graph.CreateTexture("second", description);
        graph.AddPass("first", first, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(1, 0, 0, 1)),
                ])
                .EndRendering(),
                GpuRenderGraphPassFlags.NeverCull)
            .Write(first, GpuStage.ColorOutput);
        graph.AddPass("second", second, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(0, 1, 0, 1)),
                ])
                .EndRendering())
            .Write(second, GpuStage.ColorOutput);
        graph.ExportTexture(second);

        GpuRenderGraphPlan plan = graph.Compile();
        using GpuRenderGraphExecution execution = plan.Execute(backend);
        byte[] pixels = ReadPixels(backend, execution.GetTexture(second).Texture);

        Assert.Single(plan.TransientSlots);
        Assert.Single(plan.AliasBarriers);
        AssertPixelNear(pixels, PixelWidth / 2, PixelHeight / 2, 0, 255, 0, 255);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void BackendRequirementsAllowDifferentTextureDescriptionsToAliasSafely()
    {
        using IGpuBackend backend = CreateBackend();
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource small = graph.CreateTexture(
            "small",
            new(
                PixelWidth / 2,
                PixelHeight / 2,
                GpuFormat.Rgba8Unorm,
                GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource));
        GpuRenderGraphResource large = graph.CreateTexture(
            "large",
            new(
                PixelWidth,
                PixelHeight,
                GpuFormat.Rgba8Unorm,
                GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource));
        graph.AddPass("small", small, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(1, 0, 0, 1)),
                ])
                .EndRendering(),
                GpuRenderGraphPassFlags.NeverCull)
            .Write(small, GpuStage.ColorOutput);
        graph.AddPass("large", large, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(0, 1, 0, 1)),
                ])
                .EndRendering())
            .Write(large, GpuStage.ColorOutput);
        graph.ExportTexture(large);

        GpuRenderGraphPlan plan = graph.Compile();
        if ((backend.Capabilities & GpuBackendCapabilities.MemoryAliasing) != 0)
        {
            GpuRenderGraphMemoryPlan memoryPlan = plan.CreateMemoryPlan(backend);
            Assert.Equal(2, plan.TransientSlots.Count);
            Assert.Single(memoryPlan.Slots);
            Assert.Single(memoryPlan.AliasBarriers);
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => plan.CreateMemoryPlan(backend));
        }

        using GpuRenderGraphExecution execution = plan.Execute(backend);
        byte[] pixels = ReadPixels(backend, execution.GetTexture(large).Texture);

        AssertPixelNear(pixels, PixelWidth / 2, PixelHeight / 2, 0, 255, 0, 255);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void OverlappingTransientTexturesUseDistinctArenaRegions()
    {
        using IGpuBackend backend = CreateBackend();
        var description = new GpuTextureDescription(
            PixelWidth,
            PixelHeight,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource first = graph.CreateTexture("first", description);
        GpuRenderGraphResource second = graph.CreateTexture("second", description);
        graph.AddPass("first", first, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(1, 0, 0, 1)),
                ])
                .EndRendering())
            .Write(first, GpuStage.ColorOutput);
        graph.AddPass("second", second, static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(state),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(0, 1, 0, 1)),
                ])
                .EndRendering())
            .Write(second, GpuStage.ColorOutput);
        graph.ExportTexture(first);
        graph.ExportTexture(second);

        GpuRenderGraphPlan plan = graph.Compile();
        using GpuRenderGraphExecution execution = plan.Execute(backend);
        byte[] firstPixels = ReadPixels(backend, execution.GetTexture(first).Texture);
        byte[] secondPixels = ReadPixels(backend, execution.GetTexture(second).Texture);

        Assert.Equal(2, plan.TransientSlots.Count);
        AssertPixelNear(firstPixels, PixelWidth / 2, PixelHeight / 2, 255, 0, 0, 255);
        AssertPixelNear(secondPixels, PixelWidth / 2, PixelHeight / 2, 0, 255, 0, 255);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void NonOverlappingTransientBuffersReuseAPlannedSlot()
    {
        using IGpuBackend backend = CreateBackend();
        var graph = new GpuRenderGraph();
        var description = new GpuBufferDescription(256, GpuBufferUsage.ShaderData);
        GpuRenderGraphResource first = graph.CreateBuffer("first", description);
        GpuRenderGraphResource second = graph.CreateBuffer("second", description);
        graph.AddPass("first", first, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(first, GpuStage.ComputeShader);
        graph.AddPass("second", second, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(second, GpuStage.ComputeShader);

        GpuRenderGraphPlan plan = graph.Compile();
        Assert.Single(plan.TransientSlots);
        Assert.Single(plan.AliasBarriers);

        if ((backend.Capabilities & GpuBackendCapabilities.MemoryAliasing) != 0)
        {
            using GpuRenderGraphExecution execution = plan.Execute(backend);
        }
    }

    protected abstract IGpuBackend CreateBackend();

    private static GpuRenderGraph CreateClearGraph(GpuClearColor clearColor)
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource texture = graph.CreateTexture(
            "transient",
            new(
                PixelWidth,
                PixelHeight,
                GpuFormat.Rgba8Unorm,
                GpuTextureUsage.ColorAttachment));
        graph.AddPass("clear", (Texture: texture, Color: clearColor), static (context, state) =>
        {
            GpuTextureView view = context.GetTextureView(state.Texture);
            context.Commands.BeginRendering([
                new(
                    view,
                    GpuAttachmentLoadOperation.Clear,
                    GpuAttachmentStoreOperation.Store,
                    state.Color),
            ]).EndRendering();
        }, GpuRenderGraphPassFlags.NeverCull).Write(texture, GpuStage.ColorOutput);
        return graph;
    }

    private static void Execute(IGpuBackend backend, GpuRenderGraphPlan plan)
    {
        IGpuQueue queue = backend.MainQueue;
        GpuCommandBuffer commands = plan.Record(queue);
        using GpuSemaphore completion = queue.CreateSemaphore();

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);
    }

    private static void AssertBarrier(
        GpuRenderGraphBarrierPlan barrier,
        string destinationPass,
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards,
        params GpuRenderGraphResource[] resources)
    {
        Assert.Equal(destinationPass, barrier.DestinationPass);
        Assert.Equal(before, barrier.Before);
        Assert.Equal(after, barrier.After);
        Assert.Equal(hazards, barrier.Hazards);
        Assert.Equal(resources, barrier.Resources);
    }

    private static void AssertPixelNear(
        ReadOnlySpan<byte> pixels,
        uint x,
        uint y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = checked((int)((y * PixelWidth + x) * 4));

        Assert.InRange(pixels[offset], red - 1, red + 1);
        Assert.InRange(pixels[offset + 1], green - 1, green + 1);
        Assert.InRange(pixels[offset + 2], blue - 1, blue + 1);
        Assert.Equal(alpha, pixels[offset + 3]);
    }

    private static byte[] ReadPixels(IGpuBackend backend, GpuTextureHandle texture)
    {
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            return backend.ReadTexture(
                texture,
                new(PixelWidth, PixelHeight, 4, PixelRowPitch));
        }

        var description = new GpuBufferDescription(
            PixelByteCount,
            GpuBufferUsage.CopyDestination);
        GpuBufferMemoryRequirements requirements =
            backend.GetBufferMemoryRequirements(description);
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostCached);
        GpuBufferHandle readback = default;
        try
        {
            readback = backend.CreatePlacedBuffer(description, allocation);
            GpuMemoryAddress address = backend.GetBufferMemoryAddress(
                readback,
                0,
                PixelByteCount);
            GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording()
                .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                .CopyTextureToMemory(
                    texture,
                    address,
                    new(PixelWidth, PixelHeight, 4, PixelRowPitch));
            using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
            backend.MainQueue.Submit([commands], completion, 1);
            backend.MainQueue.Wait(completion, 1);
            return allocation.MappedBytes()[..checked((int)PixelByteCount)].ToArray();
        }
        finally
        {
            if (!readback.IsNull) { backend.DestroyBuffer(readback); }
            backend.FreeMemory(allocation);
        }
    }

    private sealed class TestTexture : IDisposable
    {
        private readonly IGpuBackend backend;
        private readonly GpuMemoryAllocation? allocation;

        private TestTexture(
            IGpuBackend backend,
            GpuTextureHandle handle,
            GpuMemoryAllocation? allocation)
        {
            this.backend = backend;
            Handle = handle;
            this.allocation = allocation;
        }

        public GpuTextureHandle Handle { get; }

        public static TestTexture Create(IGpuBackend backend)
        {
            var description = new GpuTextureDescription(
                1,
                1,
                GpuFormat.Rgba8Unorm,
                GpuTextureUsage.CopySource | GpuTextureUsage.CopyDestination);

            if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
            {
                return new(backend, backend.CreateTexture(description), null);
            }
            if ((backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) != 0)
            {
                GpuTextureMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description);
                GpuMemoryAllocation allocation = backend.AllocateMemory(
                    requirements.Size,
                    requirements.Alignment,
                    GpuMemoryKind.DeviceLocal);
                try
                {
                    return new(backend, backend.CreatePlacedTexture(description, allocation), allocation);
                }
                catch
                {
                    backend.FreeMemory(allocation);
                    throw;
                }
            }

            throw new NotSupportedException("The backend cannot create a texture for render-graph conformance.");
        }

        public void Dispose()
        {
            backend.DestroyTexture(Handle);
            if (allocation is { } memory)
            {
                backend.FreeMemory(memory);
            }
        }
    }

}
