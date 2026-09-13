using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Library;
using Lumyte.Graphics;
using Lumyte.Graphics.RenderGraph.Legacy;

namespace Lumyte.Graphics.Tests;

public abstract class GpuRenderGraphBackendConformanceTests
{
    private const uint PixelWidth = 64;
    private const uint PixelHeight = 64;
    private const ulong PixelRowPitch = PixelWidth * 4;
    private const ulong PixelByteCount = PixelRowPitch * PixelHeight;

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void ForeignSecondCommandDoesNotSubmitTheFirst()
    {
        using IGpuBackend first = CreateBackend();
        using IGpuBackend second = CreateBackend();
        using GpuCommandBuffer a = first.MainQueue.StartCommandRecording();
        using GpuCommandBuffer b = second.MainQueue.StartCommandRecording();
        using GpuSemaphore completion = first.MainQueue.CreateSemaphore();

        Assert.Throws<ArgumentException>(() => first.MainQueue.Submit([a, b], completion, 1));

        Assert.Equal(GpuCommandBufferState.Recording, a.State);
        first.MainQueue.Submit([a], completion, 1);
        first.MainQueue.Wait(completion, 1);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void SubmittedSecondCommandDoesNotSubmitTheFirst()
    {
        using IGpuBackend backend = CreateBackend();
        IGpuQueue queue = backend.MainQueue;
        using GpuCommandBuffer first = queue.StartCommandRecording();
        using GpuCommandBuffer second = queue.StartCommandRecording();
        using GpuSemaphore completion = queue.CreateSemaphore();
        queue.Submit([second], completion, 1);
        queue.Wait(completion, 1);

        Assert.Throws<InvalidOperationException>(() => queue.Submit([first, second], completion, 2));

        Assert.Equal(GpuCommandBufferState.Recording, first.State);
        queue.Submit([first], completion, 2);
        queue.Wait(completion, 2);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void DuplicateCommandDoesNotConsumeTheRecording()
    {
        using IGpuBackend backend = CreateBackend();
        IGpuQueue queue = backend.MainQueue;
        using GpuCommandBuffer commands = queue.StartCommandRecording();
        using GpuSemaphore completion = queue.CreateSemaphore();

        Assert.Throws<ArgumentException>(() => queue.Submit([commands, commands], completion, 1));

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);
        Assert.Equal(GpuCommandBufferState.Submitted, commands.State);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void TextureFromAnotherDeviceCannotDestroyALocalTexture()
    {
        using IGpuBackend first = CreateBackend();
        using IGpuBackend second = CreateBackend();
        using var a = TestTexture.Create(first);
        using var b = TestTexture.Create(second);

        Assert.Throws<ArgumentException>(() => second.DestroyTexture(a.Handle));

        GpuTextureView view = second.CreateTextureView(b.Handle, new(GpuFormat.Rgba8Unorm));
        second.DestroyTextureView(view);
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void SamplerFromAnotherDeviceCannotDestroyALocalSampler()
    {
        using IGpuBackend first = CreateBackend();
        using IGpuBackend second = CreateBackend();
        SamplerId a = first.CreateSampler(new());
        SamplerId b = second.CreateSampler(new());
        try
        {
            Assert.Throws<ArgumentException>(() => second.DestroySampler(a));
        }
        finally
        {
            first.DestroySampler(a);
            second.DestroySampler(b);
        }
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void FailedRecordingDoesNotChangeTheNextRenderingState()
    {
        using IGpuBackend backend = CreateBackend();
        using var texture = TestTexture.Create(backend, PixelWidth);
        var graph = new GpuRenderGraph();
        var target = graph.ImportTexture("target", texture.Handle, texture.Description);
        GpuCommandBuffer? aborted = null;
        graph.AddPass("failure", target, (context, state) =>
        {
            aborted = context.Commands;
            context.Commands.BeginRendering([new(context.GetTextureView(state),
                GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store, new(1, 0, 0, 1))]);
            throw new InvalidOperationException("recording failed");
        }, GpuRenderGraphPassFlags.NeverCull).Write(target, GpuStage.ColorOutput);

        var failure = Assert.Throws<InvalidOperationException>(() => graph.Compile().Execute(backend));

        Assert.Equal("recording failed", failure.Message);
        Assert.NotNull(aborted);
        Assert.Equal(GpuCommandBufferState.Aborted, aborted.State);
        var valid = new GpuRenderGraph();
        var output = valid.ImportTexture("target", texture.Handle, texture.Description);
        valid.AddPass("clear", output, static (context, state) => context.Commands
            .BeginRendering([new(context.GetTextureView(state), GpuAttachmentLoadOperation.Clear,
                GpuAttachmentStoreOperation.Store, new(0, 1, 0, 1))]).EndRendering(),
            GpuRenderGraphPassFlags.NeverCull).Write(output, GpuStage.ColorOutput);
        using GpuRenderGraphExecution execution = valid.Compile().Execute(backend);
        Assert.Equal(new byte[] {0, 255, 0, 255}, ReadPixels(backend, texture.Handle)[..4]);
    }

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
        var texture = graph.CreateTexture(
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
        var sourceResource = graph.ImportTexture("source", source.Handle, source.Description);
        var outputResource = graph.ImportTexture("output", output.Handle, output.Description);
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
        var outputResource = graph.ImportTexture("output", output.Handle, output.Description);
        var unusedResource = graph.ImportTexture("unused", unused.Handle, unused.Description);
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
        var resource = graph.ImportTexture("shared", texture.Handle, texture.Description);
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
        var sourceResource = graph.ImportTexture("source", source.Handle, source.Description);
        var outputResource = graph.ImportTexture("output", output.Handle, output.Description);
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
                resourceCount: 1),
            barrier => AssertBarrier(
                barrier,
                "draw",
                GpuStage.Copy,
                GpuStage.PixelShader | GpuStage.ColorOutput,
                GpuBarrierHazards.Descriptors,
                resourceCount: 2));
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
        var transient = producer.CreateTexture("transient", description);
        var unused = producer.CreateTexture("unused", description);
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
        var imported = consumer.ImportTexture("imported", exported);
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
        var first = graph.CreateTexture("first", description);
        var second = graph.CreateTexture("second", description);
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
        var small = graph.CreateTexture(
            "small",
            new(
                PixelWidth / 2,
                PixelHeight / 2,
                GpuFormat.Rgba8Unorm,
                GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource));
        var large = graph.CreateTexture(
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
        var first = graph.CreateTexture("first", description);
        var second = graph.CreateTexture("second", description);
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
        var first = graph.CreateBuffer("first", description);
        var second = graph.CreateBuffer("second", description);
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

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void DrawInputsRemainDistinctAcrossPipelineSwitches()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = TestTexture.Create(backend, PixelWidth);
        GpuTextureView view = backend.CreateTextureView(target.Handle, new(GpuFormat.Rgba8Unorm));
        GpuRasterPipelineHandle first = default;
        GpuRasterPipelineHandle second = default;
        TestBuffer? parameterBuffer = null;
        GpuBufferView parameterView = default;
        try
        {
            GpuShaderPackage package = LoadInputs("InputRaster");
            var description = new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]);
            first = backend.CreateRasterPipeline(description, package, "vertexMain", "pixelMain", GpuShaderBindingConvention.AbiHash);
            second = backend.CreateRasterPipeline(description, package, "vertexMain", "pixelMain", GpuShaderBindingConvention.AbiHash);
            parameterBuffer = TestBuffer.Create(backend, 32);
            parameterView = backend.CreateBufferView(parameterBuffer.Handle, default);
            backend.WriteBuffer(parameterBuffer.Handle, [.. Bytes(new Vector4(1, 1, 0, 0)), .. Bytes(new Vector4(1, 1, 0, 0))]);
            var table = new GpuResourceTable(0, 0, 1);
            table.SetBuffer(0, parameterView.Id);
            using var commands = backend.MainQueue.StartCommandRecording();
            commands.BeginRendering([new(view, GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store, default)])
                .SetPipeline(first)
                .SetResourceTable(table)
                .SetViewportAndScissor(new(0, 0, PixelWidth, PixelHeight), new(0, 0, PixelWidth, PixelHeight))
                .SetRootData(RasterRoot(0, new Vector4(1, 0, 0, 1)))
                .Draw(3)
                .SetPipeline(second)
                .SetResourceTable(table)
                .SetViewportAndScissor(new(0, 0, PixelWidth, PixelHeight), new(PixelWidth / 2, 0, PixelWidth / 2, PixelHeight))
                .SetRootData(RasterRoot(1, new Vector4(0, 1, 0, 1)))
                .Draw(3).EndRendering();
            using var completion = backend.MainQueue.CreateSemaphore();
            backend.MainQueue.Submit([commands], completion, 1);
            backend.MainQueue.Wait(completion, 1);

            byte[] pixels = ReadPixels(backend, target.Handle);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.AsSpan(16 * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels.AsSpan(48 * 4, 4).ToArray());
        }
        finally
        {
            if (!parameterView.Id.IsNull) { backend.DestroyBufferView(parameterView); }
            parameterBuffer?.Dispose();
            if (!second.IsNull) { backend.DestroyRasterPipeline(second); }
            if (!first.IsNull) { backend.DestroyRasterPipeline(first); }
            backend.DestroyTextureView(view);
        }
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void ComputeRootInputsRemainDistinctAcrossSubmissions()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = TestTexture.Create(backend, PixelWidth, GpuTextureUsage.Storage);
        var view = backend.CreateTextureView(target.Handle, new(GpuFormat.Rgba8Unorm, Access: GpuTextureViewAccess.ReadWrite));
        GpuShaderPackage package = LoadInputs("InputCompute");
        GpuComputePipelineHandle pipeline = default;
        GpuComputePipelineHandle alternate = default;
        TestBuffer? parameterBuffer = null;
        GpuBufferView parameterView = default;
        try
        {
            parameterBuffer = TestBuffer.Create(backend, 32);
            parameterView = backend.CreateBufferView(parameterBuffer.Handle, default);
            var table = new GpuResourceTable(0, 0, bufferSlotCount: 1, storageTextureSlotCount: 1);
            table.SetStorageTexture(0, view.Id);
            table.SetBuffer(0, parameterView.Id);
            backend.WriteBuffer(parameterBuffer.Handle, [.. Bytes(new Vector4(1, 0, 0, 1)), .. Bytes(new Vector4(0, 1, 0, 1))]);
            pipeline = backend.CreateComputePipeline(package, "computeMain", GpuShaderBindingConvention.AbiHash);
            alternate = backend.CreateComputePipeline(package, "computeMain", GpuShaderBindingConvention.AbiHash);
            using var completion = backend.MainQueue.CreateSemaphore();
            for (uint submission = 0; submission < 2; submission++)
            {
                using var commands = backend.MainQueue.StartCommandRecording();
                commands.SetComputePipeline(pipeline).SetComputeResourceTable(table);
                commands.SetComputeRootData(ComputeRoot(submission * 2, 0, new Vector4(0, 0, 1, 0))).Dispatch(1)
                    .SetComputePipeline(alternate).SetComputeResourceTable(table)
                    .SetComputeRootData(ComputeRoot(submission * 2 + 1, 1, Vector4.Zero)).Dispatch(1);
                backend.MainQueue.Submit([commands], completion, submission + 1);
            }
            backend.MainQueue.Wait(completion, 2);

            byte[] pixels = ReadPixels(backend, target.Handle);
            Assert.Equal(new byte[] { 255, 0, 255, 255, 0, 255, 0, 255, 255, 0, 255, 255, 0, 255, 0, 255 }, pixels[..16]);
        }
        finally
        {
            if (!parameterView.Id.IsNull) { backend.DestroyBufferView(parameterView); }
            parameterBuffer?.Dispose();
            if (!alternate.IsNull) { backend.DestroyComputePipeline(alternate); }
            if (!pipeline.IsNull) { backend.DestroyComputePipeline(pipeline); }
            backend.DestroyTextureView(view);
        }
    }

    [Fact]
    [Trait("Category", "RenderGraphConformance")]
    public void StandardAddDrawAppliesBothMatrices()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = TestTexture.Create(backend, PixelWidth);
        var view = backend.CreateTextureView(target.Handle, new(GpuFormat.Rgba8Unorm));
        GpuRasterPipelineHandle pipeline = default;
        try
        {
            pipeline = backend.CreateRasterPipeline(new([new(GpuFormat.Rgba8Unorm)]),
                StandardDrawShaders.Load(), "vertexMain", "pixelMain", GpuShaderBindingConvention.AbiHash);
            var graph = new GpuRenderGraph();
            // Translation followed by scale must preserve multiplication order and row-major packing.
            graph.AddDraw("transformed", new(new(pipeline), new(3),
                    new(Matrix4x4.CreateTranslation(1, 0, 0), Matrix4x4.CreateScale(0.5f, 1, 1))),
                new(view, target.Description, GpuAttachmentLoadOperation.Clear));
            using var execution = graph.Compile().Execute(backend);

            byte[] pixels = ReadPixels(backend, target.Handle);
            Assert.Equal(new byte[4], pixels.AsSpan((32 * 64 + 8) * 4, 4).ToArray());
            Assert.Equal(255, pixels[(32 * 64 + 48) * 4 + 3]);
            Assert.InRange(pixels[(32 * 64 + 48) * 4], (byte)123, (byte)139);
        }
        finally
        {
            if (!pipeline.IsNull) { backend.DestroyRasterPipeline(pipeline); }
            backend.DestroyTextureView(view);
        }
    }

    private GpuShaderPackage LoadInputs(string name)
    {
        var assembly = GetType().Assembly;
        string resource = assembly.GetManifestResourceNames().Single(value => value.EndsWith(name + ".lshp", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return GpuShaderPackage.Read(memory.ToArray());
    }

    private static byte[] Bytes(Vector4 value) => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)).ToArray();

    private static byte[] RasterRoot(uint transformIndex, Vector4 color)
    {
        byte[] result = new byte[GpuShaderBindingConvention.RootDataSize];
        BitConverter.GetBytes(transformIndex).CopyTo(result, 0);
        Bytes(color).CopyTo(result, 16);
        return result;
    }

    private static byte[] ComputeRoot(uint index, uint parameterIndex, Vector4 tail)
    {
        byte[] result = new byte[GpuShaderBindingConvention.RootDataSize];
        BitConverter.GetBytes(index).CopyTo(result, 0);
        BitConverter.GetBytes(parameterIndex).CopyTo(result, 4);
        Bytes(tail).CopyTo(result, 48);
        return result;
    }

    protected abstract IGpuBackend CreateBackend();

    private static GpuRenderGraph CreateClearGraph(GpuClearColor clearColor)
    {
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
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
        int resourceCount)
    {
        Assert.Equal(destinationPass, barrier.DestinationPass);
        Assert.Equal(before, barrier.Before);
        Assert.Equal(after, barrier.After);
        Assert.Equal(hazards, barrier.Hazards);
        Assert.Equal(resourceCount, barrier.ResourceCount);
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
            GpuTextureDescription description,
            GpuMemoryAllocation? allocation)
        {
            this.backend = backend;
            Handle = handle;
            Description = description;
            this.allocation = allocation;
        }

        public GpuTextureHandle Handle { get; }
        public GpuTextureDescription Description { get; }

        public static TestTexture Create(IGpuBackend backend, uint size = 1, GpuTextureUsage extraUsage = GpuTextureUsage.None)
        {
            var description = new GpuTextureDescription(
                size,
                size,
                GpuFormat.Rgba8Unorm,
                GpuTextureUsage.ColorAttachment | extraUsage
                    | GpuTextureUsage.CopySource
                    | GpuTextureUsage.CopyDestination);

            if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
            {
                return new(backend, backend.CreateTexture(description), description, null);
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
                    return new(backend, backend.CreatePlacedTexture(description, allocation), description, allocation);
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

    private sealed class TestBuffer : IDisposable
    {
        private readonly IGpuBackend backend;
        private readonly GpuMemoryAllocation? allocation;

        private TestBuffer(IGpuBackend backend, GpuBufferHandle handle, GpuMemoryAllocation? allocation)
        {
            this.backend = backend;
            Handle = handle;
            this.allocation = allocation;
        }

        public GpuBufferHandle Handle { get; }

        public static TestBuffer Create(IGpuBackend backend, ulong size)
        {
            if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
            {
                var ownedDescription = new GpuBufferDescription(
                    size,
                    GpuBufferUsage.ShaderData | GpuBufferUsage.CopyDestination);
                return new(backend, backend.CreateBuffer(ownedDescription), null);
            }
            var placedDescription = new GpuBufferDescription(
                size,
                GpuBufferUsage.ShaderData | GpuBufferUsage.CopySource);
            GpuBufferMemoryRequirements requirements = backend.GetBufferMemoryRequirements(placedDescription);
            GpuMemoryAllocation allocation = backend.AllocateMemory(
                requirements.Size,
                requirements.Alignment,
                GpuMemoryKind.HostMapped,
                requirements.Compatibility);
            try { return new(backend, backend.CreatePlacedBuffer(placedDescription, allocation), allocation); }
            catch { backend.FreeMemory(allocation); throw; }
        }

        public void Dispose()
        {
            backend.DestroyBuffer(Handle);
            if (allocation is { } memory) { backend.FreeMemory(memory); }
        }
    }

}
