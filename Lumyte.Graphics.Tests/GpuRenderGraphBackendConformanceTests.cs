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
    public void GraphExecutesPassesInDependencyOrder()
    {
        using IGpuBackend backend = CreateBackend();
        using var source = TestTexture.Create(backend);
        using var output = TestTexture.Create(backend);
        var executed = new List<string>();
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource sourceResource = graph.ImportTexture("source", source.Handle);
        GpuRenderGraphResource outputResource = graph.ImportTexture("output", output.Handle);
        graph.AddPass("produce", _ => executed.Add("produce"))
            .Write(sourceResource, GpuStage.Copy);
        graph.AddPass("consume", _ => executed.Add("consume"))
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
        graph.AddPass("unused", _ => executed.Add("unused"))
            .Write(unusedResource, GpuStage.ComputeShader);
        graph.AddPass("output", _ => executed.Add("output"))
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
        graph.AddPass("initial-write", _ => executed.Add("initial-write"))
            .Write(resource, GpuStage.Copy);
        graph.AddPass("read", _ => executed.Add("read"), GpuRenderGraphPassFlags.NeverCull)
            .Read(resource, GpuStage.PixelShader);
        graph.AddPass("overwrite", _ => executed.Add("overwrite"))
            .Write(resource, GpuStage.ColorOutput);
        graph.AddPass("final-read", _ => executed.Add("final-read"), GpuRenderGraphPassFlags.NeverCull)
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
        graph.AddPass("copy", _ => { })
            .Write(sourceResource, GpuStage.Copy, GpuBarrierHazards.Descriptors);
        graph.AddPass("draw", _ => { })
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
        producer.AddPass("unused", context => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(unused),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(1, 0, 0, 1)),
                ])
                .EndRendering())
            .Write(unused, GpuStage.ColorOutput);
        producer.AddPass("clear", context => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(transient),
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(0.25f, 0.5f, 0.75f, 1)),
                ])
                .EndRendering())
            .Write(transient, GpuStage.ColorOutput);
        producer.AddPass("preserve", context => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(transient),
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
        consumer.AddPass("consume", context => context.Commands
                .BeginRendering([
                    new(
                        context.GetTextureView(imported),
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

    protected abstract IGpuBackend CreateBackend();

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
