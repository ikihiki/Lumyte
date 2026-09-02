using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuRenderGraphTests
{
    [Fact]
    public void CompileKeepsTransitiveProducersAndCullsUnusedPasses()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource upload = graph.ImportBuffer("upload", new GpuBufferHandle(1, 256));
        GpuRenderGraphResource color = graph.ImportTexture("color", new GpuTextureHandle(2));
        GpuRenderGraphResource unused = graph.ImportBuffer("unused", new GpuBufferHandle(3, 256));
        graph.AddPass("upload", _ => { }).Write(upload, GpuStage.Copy);
        graph.AddPass("draw", _ => { })
            .Read(upload, GpuStage.PixelShader)
            .Write(color, GpuStage.ColorOutput);
        graph.AddPass("unused-compute", _ => { }).Write(unused, GpuStage.ComputeShader);
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
        GpuRenderGraphResource color = graph.ImportTexture("color", new GpuTextureHandle(1));
        graph.AddPass("obsolete", _ => { }).Write(color, GpuStage.ColorOutput);
        graph.AddPass("final", _ => { }).Write(color, GpuStage.ColorOutput);
        graph.MarkOutput(color);

        GpuRenderGraphPlan plan = graph.Compile();

        GpuRenderGraphPassPlan pass = Assert.Single(plan.Passes);
        Assert.Equal("final", pass.Name);
    }

    [Fact]
    public void ReadWriteKeepsThePreviousResourceProducer()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource color = graph.ImportTexture("color", new GpuTextureHandle(1));
        graph.AddPass("base", _ => { }).Write(color, GpuStage.ColorOutput);
        graph.AddPass("composite", _ => { }).ReadWrite(color, GpuStage.ColorOutput);
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
        GpuRenderGraphResource buffer = graph.ImportBuffer("readback", new GpuBufferHandle(1, 64));
        graph.AddPass("copy", _ => { }).Write(buffer, GpuStage.Copy);
        graph.AddPass("notify", _ => { }, GpuRenderGraphPassFlags.NeverCull)
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
        GpuRenderGraphResource upload = graph.ImportBuffer("upload", new GpuBufferHandle(1, 64));
        GpuRenderGraphResource color = graph.ImportTexture("color", new GpuTextureHandle(2));
        graph.AddPass("upload", _ => { })
            .Write(upload, GpuStage.Copy, GpuBarrierHazards.Descriptors);
        graph.AddPass("draw", _ => { })
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
                Assert.Equal([upload], barrier.Resources);
            },
            barrier =>
            {
                Assert.Equal("draw", barrier.DestinationPass);
                Assert.Equal(GpuStage.Copy, barrier.Before);
                Assert.Equal(GpuStage.PixelShader | GpuStage.ColorOutput, barrier.After);
                Assert.Equal(GpuBarrierHazards.Descriptors, barrier.Hazards);
                Assert.Equal([upload, color], barrier.Resources);
            });
    }

    [Fact]
    public void ConsecutiveReadsDoNotAddAnotherBarrier()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource texture = graph.ImportTexture("texture", new GpuTextureHandle(1));
        graph.AddPass("upload", _ => { }).Write(texture, GpuStage.Copy);
        graph.AddPass("pixel-read", _ => { }, GpuRenderGraphPassFlags.NeverCull)
            .Read(texture, GpuStage.PixelShader);
        graph.AddPass("vertex-read", _ => { }, GpuRenderGraphPassFlags.NeverCull)
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
        GpuRenderGraphResource color = graph.ImportTexture("color", new GpuTextureHandle(1));
        graph.AddPass("dead", _ => events.Add("dead")).Write(color, GpuStage.ComputeShader);
        graph.AddPass("draw", _ => events.Add("draw")).Write(color, GpuStage.ColorOutput);
        graph.MarkOutput(color);

        GpuCommandBuffer commands = graph.Compile().Record(new RecordingQueue(recorder));

        Assert.NotNull(commands);
        Assert.Equal(["barrier:None>ColorOutput:None", "draw"], events);
    }

    [Fact]
    public void PassRequiresReadWriteForCombinedResourceAccess()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource texture = graph.ImportTexture("texture", new GpuTextureHandle(1));
        GpuRenderGraphPassBuilder pass = graph.AddPass("composite", _ => { })
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
        GpuRenderGraphResource foreign = first.ImportTexture("foreign", new GpuTextureHandle(1));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => second.AddPass("draw", _ => { }).Read(foreign, GpuStage.PixelShader));

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public void ImportedNativeResourceCanOnlyAppearOnce()
    {
        var graph = new GpuRenderGraph();
        graph.ImportBuffer("first", new GpuBufferHandle(7, 64));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => graph.ImportBuffer("second", new GpuBufferHandle(7, 64)));

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
        GpuRenderGraphResource output = producer.CreateTexture("output", description);
        GpuRenderGraphResource unused = producer.CreateTexture("unused", description);
        producer.AddPass("unused", _ => { }).Write(unused, GpuStage.ColorOutput);
        producer.AddPass("output", _ => { }).Write(output, GpuStage.ColorOutput);
        producer.ExportTexture(output);

        GpuRenderGraphPlan producerPlan = producer.Compile();
        GpuRenderGraphExecution producerExecution = producerPlan.Execute(backend);
        GpuRenderGraphExportedTexture exported = producerExecution.GetTexture(output);
        var consumer = new GpuRenderGraph();
        GpuRenderGraphResource imported = consumer.ImportTexture("imported", exported);
        consumer.AddPass("consume", _ => { }, GpuRenderGraphPassFlags.NeverCull)
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
        GpuRenderGraphResource imported = graph.ImportTexture(
            "imported",
            new GpuTextureHandle(1));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => graph.ExportTexture(imported));

        Assert.Contains("graph-created", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportedTransientRequiresAWriter()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphResource texture = graph.CreateTexture(
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
        GpuRenderGraphResource texture = graph.CreateTexture(
            "output",
            new(1, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        graph.AddPass("write", _ => { }).Write(texture, GpuStage.ColorOutput);
        graph.ExportTexture(texture);
        GpuRenderGraphPlan plan = graph.Compile();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => plan.Record(new RecordingQueue(new RecordingCommandRecorder([]))));

        Assert.Contains("Execute", exception.Message, StringComparison.Ordinal);
    }

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
        public IGpuQueue MainQueue { get; } = new TrackingQueue();
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
        public GpuCommandBuffer StartCommandRecording() =>
            new(new RecordingCommandRecorder([]));

        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) =>
            new TrackingSemaphore(initialValue);

        public void Submit(
            ReadOnlySpan<GpuCommandBuffer> commandBuffers,
            GpuSemaphore signalSemaphore,
            ulong signalValue)
        {
            foreach (GpuCommandBuffer commands in commandBuffers) { commands.Finish(); }
            ((TrackingSemaphore)signalSemaphore).Value = signalValue;
        }

        public void Wait(GpuSemaphore semaphore, ulong value) =>
            Assert.True(((TrackingSemaphore)semaphore).Value >= value);
    }

    private sealed class TrackingSemaphore(ulong value) : GpuSemaphore
    {
        public ulong Value { get; set; } = value;
        public override void Dispose() { }
    }
}
