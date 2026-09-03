using Lumyte.Graphics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Tests;

public sealed class GpuRenderGraphTests
{
    [Fact]
    public void TypedResourcesCarryTheirDescriptions()
    {
        var graph = new GpuRenderGraph();
        GpuTextureDescription textureDescription = TextureDescription();
        GpuBufferDescription bufferDescription = BufferDescription(128);

        GpuRenderGraphTexture texture = graph.CreateTexture("texture", textureDescription);
        GpuRenderGraphBuffer buffer = graph.ImportBuffer(
            "buffer",
            new GpuBufferHandle(1, bufferDescription.Size),
            bufferDescription);

        Assert.Equal(textureDescription, texture.Description);
        Assert.Equal(bufferDescription, buffer.Description);
    }

    [Fact]
    public void DependencyOrdersPassesWithoutCreatingAGpuBarrier()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphDependency sceneComplete = graph.CreateDependency("scene-complete");
        GpuRenderGraphTexture target = graph.ImportTexture(
            "target",
            new GpuTextureHandle(1),
            TextureDescription());
        graph.AddPass("scene", 0, static (_, _) => { }).Write(sceneComplete);
        graph.AddPass("ui", 0, static (_, _) => { })
            .Read(sceneComplete)
            .Write(target, GpuStage.ColorOutput);
        graph.MarkOutput(target);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Collection(
            plan.Passes,
            pass => Assert.Equal("scene", pass.Name),
            pass => Assert.Equal("ui", pass.Name));
        Assert.Equal(1, plan.DependencyCount);
        GpuRenderGraphBarrierPlan barrier = Assert.Single(plan.Barriers);
        Assert.Equal("ui", barrier.DestinationPass);
        Assert.Equal(GpuStage.None, barrier.Before);
        Assert.Equal(GpuStage.ColorOutput, barrier.After);
        Assert.Equal(1, barrier.ResourceCount);
    }

    [Fact]
    public void CompileKeepsTransitiveProducersAndCullsUnusedPasses()
    {
        var graph = new GpuRenderGraph();
        var upload = graph.ImportBuffer("upload", new GpuBufferHandle(1, 256), BufferDescription(256));
        var color = graph.ImportTexture("color", new GpuTextureHandle(2), TextureDescription());
        var unused = graph.ImportBuffer("unused", new GpuBufferHandle(3, 256), BufferDescription(256));
        graph.AddPass("upload", upload, static (_, _) => { }).Write(upload, GpuStage.Copy);
        graph.AddPass("draw", color, static (_, _) => { })
            .Read(upload, GpuStage.PixelShader)
            .Write(color, GpuStage.ColorOutput);
        graph.AddPass("unused-compute", unused, static (_, _) => { }).Write(unused, GpuStage.ComputeShader);
        graph.MarkOutput(color);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Collection(
            plan.Passes,
            pass => Assert.Equal("upload", pass.Name),
            pass => Assert.Equal("draw", pass.Name));
    }

    [Fact]
    public void FullOverwriteCullsAnEarlierUnusedWriter()
    {
        var graph = new GpuRenderGraph();
        var color = graph.ImportTexture("color", new GpuTextureHandle(1), TextureDescription());
        graph.AddPass("obsolete", color, static (_, _) => { }).Write(color, GpuStage.ColorOutput);
        graph.AddPass("final", color, static (_, _) => { }).Write(color, GpuStage.ColorOutput);
        graph.MarkOutput(color);

        GpuRenderGraphPlan plan = graph.Compile();

        GpuRenderGraphPassPlan pass = Assert.Single(plan.Passes);
        Assert.Equal("final", pass.Name);
    }

    [Fact]
    public void ReadWriteKeepsThePreviousResourceProducer()
    {
        var graph = new GpuRenderGraph();
        var color = graph.ImportTexture("color", new GpuTextureHandle(1), TextureDescription());
        graph.AddPass("base", color, static (_, _) => { }).Write(color, GpuStage.ColorOutput);
        graph.AddPass("composite", color, static (_, _) => { }).ReadWrite(color, GpuStage.ColorOutput);
        graph.MarkOutput(color);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Collection(
            plan.Passes,
            pass => Assert.Equal("base", pass.Name),
            pass => Assert.Equal("composite", pass.Name));
    }

    [Fact]
    public void NeverCullPassKeepsItsInputProducer()
    {
        var graph = new GpuRenderGraph();
        var buffer = graph.ImportBuffer("readback", new GpuBufferHandle(1, 64), BufferDescription(64));
        graph.AddPass("copy", buffer, static (_, _) => { }).Write(buffer, GpuStage.Copy);
        graph.AddPass("notify", buffer, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Read(buffer, GpuStage.Copy);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Collection(
            plan.Passes,
            pass => Assert.Equal("copy", pass.Name),
            pass => Assert.Equal("notify", pass.Name));
    }

    [Fact]
    public void BarriersCoverInitialAndWriteDependentTransitions()
    {
        var graph = new GpuRenderGraph();
        var upload = graph.ImportBuffer("upload", new GpuBufferHandle(1, 64), BufferDescription(64));
        var color = graph.ImportTexture("color", new GpuTextureHandle(2), TextureDescription());
        graph.AddPass("upload", upload, static (_, _) => { })
            .Write(upload, GpuStage.Copy, GpuBarrierHazards.Descriptors);
        graph.AddPass("draw", color, static (_, _) => { })
            .Read(upload, GpuStage.PixelShader)
            .Write(color, GpuStage.ColorOutput);
        graph.MarkOutput(color);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Collection(
            plan.Barriers,
            barrier =>
            {
                Assert.Equal("upload", barrier.DestinationPass);
                Assert.Equal(GpuStage.None, barrier.Before);
                Assert.Equal(GpuStage.Copy, barrier.After);
                Assert.Equal(GpuBarrierHazards.Descriptors, barrier.Hazards);
                Assert.Equal([upload.Resource], barrier.Resources);
            },
            barrier =>
            {
                Assert.Equal("draw", barrier.DestinationPass);
                Assert.Equal(GpuStage.Copy, barrier.Before);
                Assert.Equal(GpuStage.PixelShader | GpuStage.ColorOutput, barrier.After);
                Assert.Equal(GpuBarrierHazards.Descriptors, barrier.Hazards);
                Assert.Equal([upload.Resource, color.Resource], barrier.Resources);
            });
    }

    [Fact]
    public void ConsecutiveReadsDoNotAddAnotherBarrier()
    {
        var graph = new GpuRenderGraph();
        var texture = graph.ImportTexture("texture", new GpuTextureHandle(1), TextureDescription());
        graph.AddPass("upload", texture, static (_, _) => { }).Write(texture, GpuStage.Copy);
        graph.AddPass("pixel-read", texture, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Read(texture, GpuStage.PixelShader);
        graph.AddPass("vertex-read", texture, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Read(texture, GpuStage.VertexShader);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.DoesNotContain(plan.Barriers, barrier => barrier.DestinationPass == "vertex-read");
    }

    [Fact]
    public void RecordEmitsPlannedBarriersBeforeLivePassCommands()
    {
        var events = new List<string>();
        var recorder = new RecordingCommandRecorder(events);
        var graph = new GpuRenderGraph();
        var color = graph.ImportTexture("color", new GpuTextureHandle(1), TextureDescription());
        graph.AddPass("dead", events, static (_, state) => state.Add("dead")).Write(color, GpuStage.ComputeShader);
        graph.AddPass("draw", events, static (_, state) => state.Add("draw")).Write(color, GpuStage.ColorOutput);
        graph.MarkOutput(color);

        GpuCommandBuffer commands = graph.Compile().Record(new RecordingQueue(recorder));

        Assert.NotNull(commands);
        Assert.Equal(["barrier:None>ColorOutput:None", "draw"], events);
    }

    [Fact]
    public void PassRequiresReadWriteForCombinedResourceAccess()
    {
        var graph = new GpuRenderGraph();
        var texture = graph.ImportTexture("texture", new GpuTextureHandle(1), TextureDescription());
        GpuRenderGraphPassBuilder pass = graph.AddPass("composite", texture, static (_, _) => { })
            .Read(texture, GpuStage.PixelShader);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => pass.Write(texture, GpuStage.ColorOutput));

        Assert.Contains("ReadWrite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourcesCannotCrossRenderGraphs()
    {
        var first = new GpuRenderGraph();
        var second = new GpuRenderGraph();
        var foreign = first.ImportTexture("foreign", new GpuTextureHandle(1), TextureDescription());

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => second.AddPass("draw", foreign, static (_, _) => { }).Read(foreign, GpuStage.PixelShader));

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public void ImportedNativeResourceCanOnlyAppearOnce()
    {
        var graph = new GpuRenderGraph();
        graph.ImportBuffer("first", new GpuBufferHandle(7, 64), BufferDescription(64));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => graph.ImportBuffer("second", new GpuBufferHandle(7, 64), BufferDescription(64)));

        Assert.Equal("nativeValue", exception.ParamName);
    }

    [Fact]
    public void ExecuteAllocatesOnlyLiveTransientsAndKeepsExportOwnership()
    {
        var backend = new TrackingBackend();
        var description = new GpuTextureDescription(
            4,
            4,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment);
        var producer = new GpuRenderGraph();
        var output = producer.CreateTexture("output", description);
        var unused = producer.CreateTexture("unused", description);
        producer.AddPass("unused", unused, static (_, _) => { }).Write(unused, GpuStage.ColorOutput);
        producer.AddPass("output", output, static (_, _) => { }).Write(output, GpuStage.ColorOutput);
        producer.ExportTexture(output);

        GpuRenderGraphPlan producerPlan = producer.Compile();
        GpuRenderGraphExecution producerExecution = producerPlan.Execute(backend);
        GpuRenderGraphExportedTexture exported = producerExecution.GetTexture(output);
        var consumer = new GpuRenderGraph();
        var imported = consumer.ImportTexture("imported", exported);
        consumer.AddPass("consume", imported, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Read(imported, GpuStage.PixelShader);

        using (GpuRenderGraphExecution consumerExecution = consumer.Compile().Execute(backend))
        {
            Assert.Equal(1, backend.CreatedTextureCount);
            Assert.Equal(0, backend.DestroyedTextureCount);
        }
        Assert.Equal(0, backend.DestroyedTextureCount);

        producerExecution.Dispose();

        Assert.Equal(1, backend.DestroyedTextureCount);
        Assert.Throws<ObjectDisposedException>(
            () => new GpuRenderGraph().ImportTexture("expired", exported));
        Assert.Collection(
            producerPlan.Passes,
            pass => Assert.Equal("output", pass.Name));
    }

    [Fact]
    public void OnlyGraphCreatedResourcesCanBeExported()
    {
        var graph = new GpuRenderGraph();
        var imported = graph.ImportTexture(
            "imported",
            new GpuTextureHandle(1),
            TextureDescription());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => graph.ExportTexture(imported));

        Assert.Contains("graph-created", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportedTransientRequiresAWriter()
    {
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "output",
            new(1, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.ExportTexture(texture);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(graph.Compile);

        Assert.Contains("has no writer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientPlansRequireBackendExecution()
    {
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "output",
            new(1, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("write", texture, static (_, _) => { }).Write(texture, GpuStage.ColorOutput);
        graph.ExportTexture(texture);
        GpuRenderGraphPlan plan = graph.Compile();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => plan.Record(new RecordingQueue(new RecordingCommandRecorder([]))));

        Assert.Contains("Execute", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileReportsLiveTransientLifetimesInExecutionOrder()
    {
        var graph = new GpuRenderGraph();
        var description = new GpuTextureDescription(
            4,
            4,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.Sampled);
        var intermediate = graph.CreateTexture("intermediate", description);
        var output = graph.CreateTexture("output", description);
        var unused = graph.CreateTexture("unused", description);
        graph.AddPass("produce", intermediate, static (_, _) => { }).Write(intermediate, GpuStage.ColorOutput);
        graph.AddPass("unused", unused, static (_, _) => { }).Write(unused, GpuStage.ColorOutput);
        graph.AddPass("consume", output, static (_, _) => { })
            .Read(intermediate, GpuStage.PixelShader)
            .Write(output, GpuStage.ColorOutput);
        graph.ExportTexture(output);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Collection(
            plan.TransientResources,
            resource =>
            {
                Assert.Equal(intermediate.Resource, resource.Resource);
                Assert.Equal(new GpuTransientLifetime(0, 1), resource.Lifetime);
            },
            resource =>
            {
                Assert.Equal(output.Resource, resource.Resource);
                Assert.Equal(new GpuTransientLifetime(1, 1), resource.Lifetime);
            });
        Assert.DoesNotContain(plan.TransientResources, resource => resource.Resource == unused.Resource);
    }

    [Fact]
    public void CompatibleNonOverlappingTransientsShareAReuseSlot()
    {
        var graph = new GpuRenderGraph();
        var description = new GpuBufferDescription(64, GpuBufferUsage.ShaderData);
        var first = graph.CreateBuffer("first", description);
        var second = graph.CreateBuffer("second", description);
        graph.AddPass("first", first, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(first, GpuStage.ComputeShader);
        graph.AddPass("second", second, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(second, GpuStage.ComputeShader);

        GpuRenderGraphPlan plan = graph.Compile();

        GpuRenderGraphTransientSlotPlan slot = Assert.Single(plan.TransientSlots);
        Assert.Equal([first.Resource, second.Resource], slot.Resources);
        Assert.All(plan.TransientResources, resource => Assert.Equal(slot.Slot, resource.ReuseSlot));
        Assert.Equal(GpuRenderGraphResourceKind.Buffer, slot.Kind);
        Assert.Equal(description, slot.BufferDescription);
        GpuRenderGraphAliasBarrierPlan alias = Assert.Single(plan.AliasBarriers);
        Assert.Equal("second", alias.DestinationPass);
        Assert.Equal(first.Resource, alias.BeforeResource);
        Assert.Equal(second.Resource, alias.AfterResource);
    }

    [Fact]
    public void OverlappingTransientLifetimesUseDifferentReuseSlots()
    {
        var graph = new GpuRenderGraph();
        var description = new GpuBufferDescription(64, GpuBufferUsage.ShaderData);
        var first = graph.CreateBuffer("first", description);
        var second = graph.CreateBuffer("second", description);
        graph.AddPass("produce-first", first, static (_, _) => { }).Write(first, GpuStage.ComputeShader);
        graph.AddPass("produce-second", second, static (_, _) => { }).Write(second, GpuStage.ComputeShader);
        graph.AddPass("consume", first, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Read(first, GpuStage.ComputeShader)
            .Read(second, GpuStage.ComputeShader);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Equal(2, plan.TransientSlots.Count);
        Assert.NotEqual(
            plan.TransientResources.Single(resource => resource.Resource == first.Resource).ReuseSlot,
            plan.TransientResources.Single(resource => resource.Resource == second.Resource).ReuseSlot);
    }

    [Fact]
    public void IncompatibleTransientDescriptionsUseDifferentReuseSlots()
    {
        var graph = new GpuRenderGraph();
        var small = graph.CreateBuffer(
            "small",
            new(64, GpuBufferUsage.ShaderData));
        var large = graph.CreateBuffer(
            "large",
            new(128, GpuBufferUsage.ShaderData));
        graph.AddPass("small", small, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(small, GpuStage.ComputeShader);
        graph.AddPass("large", large, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(large, GpuStage.ComputeShader);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Equal(2, plan.TransientSlots.Count);
    }

    [Fact]
    public void PhysicalPlanReusesTheLargestCompatibleRequirement()
    {
        var backend = new AliasingTrackingBackend(description => description.Width switch
        {
            4 => new(64, 16, 7),
            8 => new(256, 64, 7),
            _ => throw new InvalidOperationException(),
        });
        using var retirements = new GpuRetirementQueue(backend);
        var cache = new GpuRenderGraphPlanCache();
        var graph = new GpuRenderGraph();
        var small = graph.CreateTexture(
            "small",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        var large = graph.CreateTexture(
            "large",
            new(8, 8, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("small", small, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(small, GpuStage.ColorOutput);
        graph.AddPass("large", large, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(large, GpuStage.ColorOutput);
        graph.Compile(cache);

        GpuRenderGraphPlan plan = graph.Compile(cache);
        GpuRenderGraphMemoryPlan memoryPlan = plan.CreateMemoryPlan(backend);
        using GpuRenderGraphExecution execution = plan.ExecuteAsync(backend, retirements);

        Assert.Equal(2, plan.TransientSlots.Count);
        GpuRenderGraphPhysicalSlotPlan slot = Assert.Single(memoryPlan.Slots);
        Assert.Equal(256ul, slot.Size);
        Assert.Equal(64ul, slot.Alignment);
        Assert.Equal(7ul, slot.Compatibility);
        Assert.All(memoryPlan.Resources, resource => Assert.Equal(slot.Slot, resource.ReuseSlot));
        Assert.Equal(1, cache.HitCount);
        Assert.Equal(1, cache.MissCount);
        Assert.Collection(
            backend.Placements,
            placement => Assert.Equal(new GpuMemoryAddress(1, 0, 256), placement.MemoryAddress),
            placement => Assert.Equal(new GpuMemoryAddress(1, 0, 256), placement.MemoryAddress));

        backend.Queue.Complete(execution.Completion.Value);
        retirements.Collect();
        Assert.Equal(2, backend.DestroyedTextureCount);
    }

    [Fact]
    public void PhysicalPlanSeparatesIncompatibleRequirements()
    {
        var backend = new AliasingTrackingBackend(description =>
            new(64, 16, description.Usage == GpuTextureUsage.Sampled ? 1ul : 2ul));
        var graph = new GpuRenderGraph();
        var sampled = graph.CreateTexture(
            "sampled",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        var attachment = graph.CreateTexture(
            "attachment",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("sampled", sampled, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(sampled, GpuStage.ComputeShader);
        graph.AddPass("attachment", attachment, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(attachment, GpuStage.ColorOutput);

        GpuRenderGraphMemoryPlan memoryPlan = graph.Compile().CreateMemoryPlan(backend);

        Assert.Equal(2, memoryPlan.Slots.Count);
        Assert.Empty(memoryPlan.AliasBarriers);
    }

    [Fact]
    public void PhysicalPlanIntersectsNativeCompatibilityMasks()
    {
        var backend = new AliasingTrackingBackend(
            description => new(
                64,
                16,
                description.Usage == GpuTextureUsage.Sampled ? 0b0110ul : 0b1010ul),
            intersectsCompatibilityMasks: true);
        var graph = new GpuRenderGraph();
        var sampled = graph.CreateTexture(
            "sampled",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        var attachment = graph.CreateTexture(
            "attachment",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("sampled", sampled, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(sampled, GpuStage.ComputeShader);
        graph.AddPass("attachment", attachment, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(attachment, GpuStage.ColorOutput);

        GpuRenderGraphMemoryPlan memoryPlan = graph.Compile().CreateMemoryPlan(backend);

        GpuRenderGraphPhysicalSlotPlan slot = Assert.Single(memoryPlan.Slots);
        Assert.Equal(0b0010ul, slot.Compatibility);
        Assert.Single(memoryPlan.AliasBarriers);
    }

    [Fact]
    public void PhysicalPlanSeparatesResourceAndMemoryKinds()
    {
        var backend = new AliasingTrackingBackend();
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "texture",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.Storage));
        var deviceBuffer = graph.CreateBuffer(
            "device-buffer",
            new(64, GpuBufferUsage.ShaderData));
        var upload = graph.CreateBuffer(
            "upload",
            new(64, GpuBufferUsage.CopySource),
            GpuMemoryKind.HostMapped);
        var readback = graph.CreateBuffer(
            "readback",
            new(64, GpuBufferUsage.CopyDestination),
            GpuMemoryKind.HostCached);
        graph.AddPass("texture", texture, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(texture, GpuStage.ComputeShader);
        graph.AddPass("device-buffer", deviceBuffer, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(deviceBuffer, GpuStage.ComputeShader);
        graph.AddPass("upload", upload, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(upload, GpuStage.Copy);
        graph.AddPass("readback", readback, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(readback, GpuStage.Copy);

        GpuRenderGraphMemoryPlan memoryPlan = graph.Compile().CreateMemoryPlan(backend);

        Assert.Equal(4, memoryPlan.Slots.Count);
    }

    [Fact]
    public void DifferentMemoryKindsUseDifferentReuseSlots()
    {
        var graph = new GpuRenderGraph();
        var description = new GpuBufferDescription(64, GpuBufferUsage.CopyDestination);
        var upload = graph.CreateBuffer(
            "upload",
            description,
            GpuMemoryKind.HostMapped);
        var readback = graph.CreateBuffer(
            "readback",
            description,
            GpuMemoryKind.HostCached);
        graph.AddPass("upload", upload, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(upload, GpuStage.Copy);
        graph.AddPass("readback", readback, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(readback, GpuStage.Copy);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Equal(2, plan.TransientSlots.Count);
        Assert.Collection(
            plan.TransientSlots,
            slot => Assert.Equal(GpuMemoryKind.HostMapped, slot.MemoryKind),
            slot => Assert.Equal(GpuMemoryKind.HostCached, slot.MemoryKind));
    }

    [Fact]
    public void ExportedTransientLifetimeExtendsToTheEndOfThePlan()
    {
        var graph = new GpuRenderGraph();
        var description = new GpuTextureDescription(
            4,
            4,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment);
        var exported = graph.CreateTexture("exported", description);
        var scratch = graph.CreateTexture("scratch", description);
        graph.AddPass("export", exported, static (_, _) => { }).Write(exported, GpuStage.ColorOutput);
        graph.AddPass("scratch", scratch, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(scratch, GpuStage.ColorOutput);
        graph.ExportTexture(exported);

        GpuRenderGraphPlan plan = graph.Compile();

        GpuRenderGraphTransientResourcePlan exportedPlan = plan.TransientResources.Single(
            resource => resource.Resource == exported.Resource);
        GpuRenderGraphTransientResourcePlan scratchPlan = plan.TransientResources.Single(
            resource => resource.Resource == scratch.Resource);
        Assert.Equal(new GpuTransientLifetime(0, 1), exportedPlan.Lifetime);
        Assert.NotEqual(exportedPlan.ReuseSlot, scratchPlan.ReuseSlot);
    }

    [Fact]
    public void ExecutePlacesAReuseSlotInOneArenaRegionAndRecordsItsAliasBarrier()
    {
        var backend = new AliasingTrackingBackend();
        var graph = new GpuRenderGraph();
        var description = new GpuTextureDescription(
            4,
            4,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment);
        var first = graph.CreateTexture("first", description);
        var second = graph.CreateTexture("second", description);
        graph.AddPass("first", first, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(first, GpuStage.ColorOutput);
        graph.AddPass("second", second, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(second, GpuStage.ColorOutput);

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);

        Assert.Equal(1, backend.AllocatedBlockCount);
        Assert.Equal(1, backend.FreedBlockCount);
        Assert.Equal(2, backend.DestroyedTextureCount);
        Assert.Collection(
            backend.Placements,
            placement => Assert.Equal(new GpuMemoryAddress(1, 0, 64), placement.MemoryAddress),
            placement => Assert.Equal(new GpuMemoryAddress(1, 0, 64), placement.MemoryAddress));
        Assert.Contains(
            "alias:ColorOutput>ColorOutput:None",
            backend.Events);
    }

    [Fact]
    public void CallerOwnedArenaReusesItsBackingBlockAcrossExecutions()
    {
        var backend = new AliasingTrackingBackend();
        using var arena = new GpuPersistentArena(backend, 1024);
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "texture",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("write", texture, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(texture, GpuStage.ColorOutput);
        GpuRenderGraphPlan plan = graph.Compile();

        using (plan.Execute(backend, arena)) { }
        using (plan.Execute(backend, arena)) { }

        Assert.Equal(1, backend.AllocatedBlockCount);
        Assert.Equal(0, backend.FreedBlockCount);
        arena.VerifyEmpty();
        Assert.Equal(1, backend.FreedBlockCount);
    }

    [Fact]
    public void AsyncExecutionRetiresTransientResourcesAfterCompletion()
    {
        var backend = new TrackingBackend();
        using var retirements = new GpuRetirementQueue(backend);
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "texture",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("write", texture, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(texture, GpuStage.ColorOutput);

        using GpuRenderGraphExecution execution = graph.Compile().ExecuteAsync(backend, retirements);
        GpuSubmissionToken completion = execution.Completion;

        Assert.True(completion.IsValid);
        Assert.False(execution.IsComplete);
        Assert.Equal(0, backend.DestroyedTextureCount);
        Assert.Equal(0, retirements.Collect());

        backend.Queue.Complete(completion.Value);

        Assert.True(execution.IsComplete);
        Assert.Equal(1, backend.DestroyedTextureCount);
        Assert.Equal(0, retirements.InFlightSubmissionCount);
    }

    [Fact]
    public void DisposedAsyncExportWaitsForGpuCompletion()
    {
        var backend = new TrackingBackend();
        using var retirements = new GpuRetirementQueue(backend);
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "texture",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("write", texture, static (_, _) => { }).Write(texture, GpuStage.ColorOutput);
        graph.ExportTexture(texture);
        GpuRenderGraphExecution execution = graph.Compile().ExecuteAsync(backend, retirements);
        GpuRenderGraphExportedTexture exported = execution.GetTexture(texture);
        GpuSubmissionToken completion = execution.Completion;

        execution.Dispose();

        Assert.Equal(0, backend.DestroyedTextureCount);
        Assert.Throws<ObjectDisposedException>(
            () => new GpuRenderGraph().ImportTexture("expired", exported));

        backend.Queue.Complete(completion.Value);
        retirements.Collect();

        Assert.Equal(1, backend.DestroyedTextureCount);
    }

    [Fact]
    public void CompletedAsyncExportLivesUntilExecutionIsDisposed()
    {
        var backend = new TrackingBackend();
        using var retirements = new GpuRetirementQueue(backend);
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "texture",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("write", texture, static (_, _) => { }).Write(texture, GpuStage.ColorOutput);
        graph.ExportTexture(texture);
        GpuRenderGraphExecution execution = graph.Compile().ExecuteAsync(backend, retirements);
        GpuRenderGraphExportedTexture exported = execution.GetTexture(texture);

        backend.Queue.Complete(execution.Completion.Value);
        retirements.Collect();

        var consumer = new GpuRenderGraph();
        consumer.ImportTexture("live", exported);
        Assert.Equal(0, backend.DestroyedTextureCount);

        execution.Dispose();

        Assert.Equal(1, backend.DestroyedTextureCount);
    }

    [Fact]
    public void RetirementQueueLimitsSubmissionsInFlight()
    {
        var backend = new TrackingBackend();
        using var retirements = new GpuRetirementQueue(backend, maximumFramesInFlight: 1);
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "texture",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("write", texture, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(texture, GpuStage.ColorOutput);
        GpuRenderGraphPlan plan = graph.Compile();

        using GpuRenderGraphExecution first = plan.ExecuteAsync(backend, retirements);
        using GpuRenderGraphExecution second = plan.ExecuteAsync(backend, retirements);

        Assert.Equal(1, backend.Queue.WaitCount);
        Assert.Equal(1, backend.DestroyedTextureCount);
        Assert.Equal(1, retirements.InFlightSubmissionCount);

        retirements.WaitIdle();
        Assert.Equal(2, backend.DestroyedTextureCount);
    }

    [Fact]
    public void AsyncExecutionKeepsArenaRegionUntilCompletion()
    {
        var backend = new AliasingTrackingBackend();
        using var arena = new GpuPersistentArena(backend, 1024);
        using var retirements = new GpuRetirementQueue(backend);
        var graph = new GpuRenderGraph();
        var texture = graph.CreateTexture(
            "texture",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("write", texture, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Write(texture, GpuStage.ColorOutput);

        using GpuRenderGraphExecution execution =
            graph.Compile().ExecuteAsync(backend, arena, retirements);

        Assert.Equal(1, arena.LiveAllocationCount);
        Assert.Equal(0, backend.DestroyedTextureCount);

        backend.Queue.Complete(execution.Completion.Value);
        retirements.Collect();

        Assert.Equal(0, arena.LiveAllocationCount);
        Assert.Equal(1, backend.DestroyedTextureCount);
        arena.VerifyEmpty();
    }

    [Fact]
    public void AsyncConsumerLeaseKeepsProducerExportAliveUntilConsumerCompletes()
    {
        var backend = new TrackingBackend();
        using var retirements = new GpuRetirementQueue(backend);
        var producer = new GpuRenderGraph();
        var produced = producer.CreateTexture(
            "produced",
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        producer.AddPass("produce", produced, static (_, _) => { }).Write(produced, GpuStage.ColorOutput);
        producer.ExportTexture(produced);
        GpuRenderGraphExecution producerExecution =
            producer.Compile().ExecuteAsync(backend, retirements);
        GpuRenderGraphExportedTexture exported = producerExecution.GetTexture(produced);

        var consumer = new GpuRenderGraph();
        var imported = consumer.ImportTexture("imported", exported);
        consumer.AddPass("consume", imported, static (_, _) => { }, GpuRenderGraphPassFlags.NeverCull)
            .Read(imported, GpuStage.PixelShader);
        using GpuRenderGraphExecution consumerExecution =
            consumer.Compile().ExecuteAsync(backend, retirements);

        producerExecution.Dispose();
        backend.Queue.Complete(producerExecution.Completion.Value);
        retirements.Collect();

        Assert.Equal(0, backend.DestroyedTextureCount);

        backend.Queue.Complete(consumerExecution.Completion.Value);
        retirements.Collect();

        Assert.Equal(1, backend.DestroyedTextureCount);
    }

    private static GpuTextureDescription TextureDescription() => new(
        1,
        1,
        GpuFormat.Rgba8Unorm,
        GpuTextureUsage.Sampled
            | GpuTextureUsage.Storage
            | GpuTextureUsage.ColorAttachment
            | GpuTextureUsage.CopySource
            | GpuTextureUsage.CopyDestination);

    private static GpuBufferDescription BufferDescription(ulong size) => new(
        size,
        GpuBufferUsage.CopySource
            | GpuBufferUsage.CopyDestination
            | GpuBufferUsage.ShaderData
            | GpuBufferUsage.IndirectArguments);

    private sealed class RecordingQueue(RecordingCommandRecorder recorder) : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording() => new(recorder);
        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) => throw new NotSupportedException();
        public void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue) =>
            throw new NotSupportedException();
        public void Wait(GpuSemaphore semaphore, ulong value) => throw new NotSupportedException();
    }

    private sealed class RecordingCommandRecorder(List<string> events) : IGpuCommandRecorder
    {
        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards) =>
            events.Add($"barrier:{before}>{after}:{hazards}");
        public void AliasingBarrier(
            GpuAliasingResource beforeResource,
            GpuAliasingResource afterResource,
            GpuStage before,
            GpuStage after,
            GpuBarrierHazards hazards) =>
            events.Add($"alias:{before}>{after}:{hazards}");
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

    private sealed class TrackingBackend : IGpuBackend
    {
        private readonly HashSet<GpuTextureHandle> textures = [];
        private ulong nextTexture = 1;

        public GpuBackendCapabilities Capabilities => GpuBackendCapabilities.DeviceOwnedResources;
        public TrackingQueue Queue { get; } = new();
        public IGpuQueue MainQueue => Queue;
        public int CreatedTextureCount { get; private set; }
        public int DestroyedTextureCount { get; private set; }

        public GpuTextureHandle CreateTexture(GpuTextureDescription description)
        {
            description.Validate();
            var texture = new GpuTextureHandle(nextTexture++);
            textures.Add(texture);
            CreatedTextureCount++;
            return texture;
        }

        public void DestroyTexture(GpuTextureHandle texture)
        {
            Assert.True(textures.Remove(texture), $"Texture {texture.Value} was destroyed more than once.");
            DestroyedTextureCount++;
        }
    }

    private sealed class TrackingQueue : IGpuQueue
    {
        private readonly IGpuCommandRecorder recorder;
        private TrackingSemaphore? lastSubmitted;

        public TrackingQueue() : this(new RecordingCommandRecorder([])) { }
        public TrackingQueue(IGpuCommandRecorder recorder) => this.recorder = recorder;

        public int WaitCount { get; private set; }

        public GpuCommandBuffer StartCommandRecording() => new(recorder);

        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) =>
            new TrackingSemaphore(initialValue);

        public void Submit(
            ReadOnlySpan<GpuCommandBuffer> commandBuffers,
            GpuSemaphore signalSemaphore,
            ulong signalValue)
        {
            foreach (GpuCommandBuffer commands in commandBuffers) { commands.Finish(); }
            var semaphore = (TrackingSemaphore)signalSemaphore;
            semaphore.Value = signalValue;
            lastSubmitted = semaphore;
        }

        public void Wait(GpuSemaphore semaphore, ulong value)
        {
            var tracked = (TrackingSemaphore)semaphore;
            Assert.True(tracked.Value >= value);
            WaitCount++;
            tracked.CompletedValue = Math.Max(tracked.CompletedValue, value);
        }

        public bool IsComplete(GpuSemaphore semaphore, ulong value)
        {
            var tracked = (TrackingSemaphore)semaphore;
            Assert.True(tracked.Value >= value);
            return tracked.CompletedValue >= value;
        }

        public void Complete(ulong value)
        {
            Assert.NotNull(lastSubmitted);
            lastSubmitted.CompletedValue = Math.Max(lastSubmitted.CompletedValue, value);
        }
    }

    private sealed class TrackingSemaphore(ulong value) : GpuSemaphore
    {
        public ulong Value { get; set; } = value;
        public ulong CompletedValue { get; set; } = value;
        public override void Dispose() { }
    }

    private sealed class AliasingTrackingBackend : IGpuBackend
    {
        private readonly TrackingQueue queue;
        private readonly Func<GpuTextureDescription, GpuTextureMemoryRequirements> textureRequirements;
        private readonly bool intersectsCompatibilityMasks;
        private ulong nextTexture = 1;

        public AliasingTrackingBackend(
            Func<GpuTextureDescription, GpuTextureMemoryRequirements>? textureRequirements = null,
            bool intersectsCompatibilityMasks = false)
        {
            queue = new(new RecordingCommandRecorder(Events));
            this.textureRequirements = textureRequirements ?? (_ => new(64, 16, 7));
            this.intersectsCompatibilityMasks = intersectsCompatibilityMasks;
        }

        public List<string> Events { get; } = [];
        public TrackingQueue Queue => queue;
        public List<GpuMemoryAllocation> Placements { get; } = [];
        public int AllocatedBlockCount { get; private set; }
        public int FreedBlockCount { get; private set; }
        public int DestroyedTextureCount { get; private set; }
        public GpuBackendCapabilities Capabilities =>
            GpuBackendCapabilities.ExplicitPlacement | GpuBackendCapabilities.MemoryAliasing;
        public IGpuQueue MainQueue => queue;

        public GpuTextureMemoryRequirements GetTextureMemoryRequirements(GpuTextureDescription description)
            => textureRequirements(description);

        public GpuBufferMemoryRequirements GetBufferMemoryRequirements(GpuBufferDescription description)
            => new(description.Size, 16, 7);

        public bool TryCombineMemoryCompatibility(ulong left, ulong right, out ulong combined)
        {
            if (!intersectsCompatibilityMasks)
            {
                combined = left;
                return left == right;
            }
            combined = left & right;
            return combined != 0;
        }

        public GpuMemoryAllocation AllocateMemory(
            ulong size,
            ulong alignment,
            GpuMemoryKind kind,
            ulong compatibility)
        {
            AllocatedBlockCount++;
            return new(size, alignment, kind, 0, new((ulong)AllocatedBlockCount, 0, size));
        }

        public void FreeMemory(GpuMemoryAllocation allocation) => FreedBlockCount++;

        public GpuTextureHandle CreatePlacedTexture(
            GpuTextureDescription description,
            GpuMemoryAllocation allocation)
        {
            Placements.Add(allocation);
            return new(nextTexture++);
        }

        public void DestroyTexture(GpuTextureHandle texture) => DestroyedTextureCount++;
    }
}
