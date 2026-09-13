using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Native.Resources.Tests;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph.Tests;

public sealed class NativeGraphInteropTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedOutputsWithoutFeaturePassesRetainTheirGpuUse(bool texture)
    {
        var backend = new TestResourceBackend { AutoComplete = false };
        await using var runtime = await NativeGraphPlanningTests.Create(backend, (_, _) => { });
        var graph = new GpuRenderGraph();
        var source = ImportSource(runtime, graph, texture);
        var execution = await runtime.SubmitAsync(graph.Compile());

        source.Owner.Dispose(); execution.Dispose(); runtime.Resources.Collect();

        Assert.False(source.Lease.Released.Task.IsCompleted);
        backend.CompleteAll(); await runtime.WaitIdleAsync(); await source.Lease.Released.Task;
        Assert.Equal(1, source.Lease.Disposals);
    }

    [Fact]
    public async Task ImportedRawMemoryKeepsItsLeaseUntilTheLastGpuReaderEnds()
    {
        var backend = new TestResourceBackend { AutoComplete = false };
        await using var runtime = await NativeGraphPlanningTests.Create(backend, (context, request) =>
        {
            var output = context.ImportBuffer(request.Target);
            context.AddPass("write", output, (record, value) => record.GetBufferRange(value))
                .Write(output, new(GpuStage.Copy, GpuAccess.CopyWrite));
        });
        var scope = runtime.NativeResources.Manager.CreateScope();
        var native = scope.CreateBuffer(new(16));
        var lease = new ReleaseLease(scope);
        var import = runtime.NativeResources.ImportBuffer(runtime.NativeResources.Manager.GetBufferRange(native), lease);
        var graph = new GpuRenderGraph(); var target = graph.ImportBuffer("target", import.Reference);
        graph.AddPass("write", new NativeGraphPlanningTests.Contract(), new(target)); graph.MarkOutput(target);

        var execution = await runtime.SubmitAsync(graph.Compile());
        import.Dispose(); execution.Dispose(); runtime.Resources.Collect();

        Assert.False(lease.Released.Task.IsCompleted);
        backend.CompleteAll(); await runtime.WaitIdleAsync(); await lease.Released.Task;
        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public async Task ImportedMemoryKeepsItsLeaseThroughAnAcceptedPredecessorWithoutAReader()
    {
        var backend = new TestResourceBackend { AutoComplete = false };
        await using var runtime = await NativeGraphPlanningTests.Create(backend, (_, _) => { });
        var scope = runtime.NativeResources.Manager.CreateScope(); var native = scope.CreateBuffer(new(16));
        var lease = new ReleaseLease(scope);
        using var batch = runtime.NativeResources.Manager.BeginBatch(); batch.Use(native); batch.StartCommandRecording();
        var prior = batch.Submit(); batch.Dispose();

        var import = runtime.NativeResources.ImportBuffer(runtime.NativeResources.Manager.GetBufferRange(native), lease, prior);
        import.Dispose(); runtime.Resources.Collect();

        Assert.False(lease.Released.Task.IsCompleted);
        backend.CompleteAll(); await prior.WaitAsync(); await runtime.WaitIdleAsync(); await lease.Released.Task;
    }

    [Fact]
    public async Task ForeignPredecessorsAreRejectedBeforeTakingImportOwnership()
    {
        using var backend = new TestResourceBackend();
        await using var foreign = new GpuResourceManager(backend);
        using var batch = foreign.BeginBatch(); batch.StartCommandRecording(); var token = batch.Submit(); batch.Dispose();
        await using var runtime = await NativeGraphPlanningTests.Create(new(), (_, _) => { });
        using var scope = runtime.NativeResources.Manager.CreateScope(); var buffer = scope.CreateBuffer(new(16));
        var lease = new ReleaseLease(null);

        var error = Assert.Throws<ArgumentException>(() => runtime.NativeResources.ImportBuffer(
            runtime.NativeResources.Manager.GetBufferRange(buffer), lease, token));

        Assert.Equal("token", error.ParamName);
        Assert.Equal(0, lease.Disposals);
    }

    private static (IDisposable Owner, ReleaseLease Lease)
        ImportSource(NativeRenderRuntime runtime, GpuRenderGraph graph, bool texture)
    {
        var manager = runtime.NativeResources.Manager;
        var scope = manager.CreateScope();
        var lease = new ReleaseLease(scope);
        if (texture)
        {
            NativeGpuTextureDescription description = new(NativeGpuTextureDimension.TwoD,
                2, 2, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.CopySource);
            var native = scope.CreateTexture(description);
            var import = runtime.NativeResources.ImportTexture(manager.GetTextureHandle(native), description, lease);
            var resource = graph.ImportTexture("import", import.Reference); graph.MarkOutput(resource);
            return (import, lease);
        }
        else
        {
            var native = scope.CreateBuffer(new(16));
            var import = runtime.NativeResources.ImportBuffer(manager.GetBufferRange(native), lease);
            var resource = graph.ImportBuffer("import", import.Reference); graph.MarkOutput(resource);
            return (import, lease);
        }
    }

    private sealed class ReleaseLease(IDisposable? value) : IDisposable
    {
        internal int Disposals;
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() { Disposals++; value?.Dispose(); Released.TrySetResult(); }
    }
}
