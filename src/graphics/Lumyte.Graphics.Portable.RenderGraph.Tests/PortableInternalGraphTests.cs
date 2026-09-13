using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

namespace Lumyte.Graphics.Portable.RenderGraph.Tests;

public sealed class PortableInternalGraphTests
{
    [Fact]
    public async Task PreservedReadKeepsItsResourceWriter()
    {
        var recorded = new List<int>();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(new(), (context, output) =>
        {
            PortablePassBuffer data = context.CreateBuffer("data", new(16, GpuBufferUsage.Storage));
            context.AddPass("writer", 1, (_, value) => recorded.Add(value)).Write(data, PortablePassUsage.StorageWrite);
            context.AddPass("side effect", 2, (_, value) => recorded.Add(value)).Read(data, PortablePassUsage.StorageRead).Preserve();
            context.AddPass("output", 3, (_, value) => recorded.Add(value)).Write(context.ImportTexture(output), PortablePassUsage.ColorAttachment);
        });

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(GraphFixture.Plan(preserved: true));
        await execution.WaitForCompletionAsync();

        Assert.Equal([1, 2, 3], recorded);
    }

    [Fact]
    public async Task InternalSideEffectsRequireTheCommonPreserveDeclaration()
    {
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(new(), (context, output) =>
            context.AddPass("side effect", 0, static (_, _) => { }).Preserve());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());

        Assert.Contains("preserved feature contract", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveReadsRequireInitializedContents(bool readWrite)
    {
        var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            PortablePassBuffer undefined = context.CreateBuffer("undefined", new(16, GpuBufferUsage.Storage));
            PortablePassBuilder pass = context.AddPass("read", 0, static (_, _) => { }).Write(context.ImportTexture(output), PortablePassUsage.ColorAttachment);
            if (readWrite) { pass.ReadWrite(undefined, PortablePassUsage.StorageWrite); }
            else { pass.Read(undefined, PortablePassUsage.StorageRead); }
        });

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());

        Assert.Contains("uninitialized", error.Message);
        Assert.Empty(backend.Created);
        Assert.Empty(backend.TestQueue.Submitted);
    }

    [Fact]
    public async Task DeadReadsAndOverwrittenWritersAreCulledBeforeAllocation()
    {
        var recorded = new List<string>(); var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            PortablePassBuffer undefined = context.CreateBuffer("dead", new(16, GpuBufferUsage.Storage));
            context.AddPass("dead read", "dead", (_, value) => recorded.Add(value)).Read(undefined, PortablePassUsage.StorageRead);
            PortablePassTexture target = context.ImportTexture(output);
            context.AddPass("overwritten", "old", (_, value) => recorded.Add(value)).Write(target, PortablePassUsage.ColorAttachment);
            context.AddPass("last", "last", (_, value) => recorded.Add(value)).Write(target, PortablePassUsage.ColorAttachment);
        });

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(GraphFixture.Plan());
        await execution.WaitForCompletionAsync();

        Assert.Equal(["last"], recorded);
        Assert.Single(backend.Created);
    }

    [Fact]
    public async Task ReadWriteKeepsThePreviousWriter()
    {
        var recorded = new List<int>(); var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            PortablePassTexture target = context.ImportTexture(output);
            context.AddPass("initialize", 1, (_, value) => recorded.Add(value)).Write(target, PortablePassUsage.ColorAttachment);
            context.AddPass("update", 2, (_, value) => recorded.Add(value)).ReadWrite(target, PortablePassUsage.ColorAttachment);
        });

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(GraphFixture.Plan());
        await execution.WaitForCompletionAsync();

        Assert.Equal([1, 2], recorded);
    }

    [Fact]
    public async Task NonoverlappingTransientsReuseTheSameObject()
    {
        var handles = new List<GpuBufferHandle>(); var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            var description = new GpuBufferDescription(16, GpuBufferUsage.Storage);
            PortablePassBuffer first = context.CreateBuffer("first", description), second = context.CreateBuffer("second", description);
            context.AddPass("first writer", first, (record, value) => handles.Add(record.GetBufferRange(value).Buffer))
                .Write(first, PortablePassUsage.StorageWrite).Preserve();
            context.AddPass("first reader", 0, static (_, _) => { }).Read(first, PortablePassUsage.StorageRead).Preserve();
            context.AddPass("second writer", second, (record, value) => handles.Add(record.GetBufferRange(value).Buffer))
                .Write(second, PortablePassUsage.StorageWrite).Preserve();
            context.AddPass("output", 0, static (_, _) => { }).Read(second, PortablePassUsage.StorageRead)
                .Write(context.ImportTexture(output), PortablePassUsage.ColorAttachment);
        });

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(GraphFixture.Plan(preserved: true));
        await execution.WaitForCompletionAsync();

        Assert.Equal(2, handles.Count);
        Assert.Same(handles[0], handles[1]);
        Assert.Single(backend.Created.OfType<ManagerTestBackend.Buffer>());
    }

    [Fact]
    public async Task CapturedBuildBuilderAndRecordContextsCloseAtTheirBoundary()
    {
        PortablePassBuildContext? build = null; PortablePassRecordContext? record = null; PortablePassBuilder? builder = null;
        PortablePassTexture? texture = null; var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            build = context; texture = context.ImportTexture(output);
            builder = context.AddPass("output", 0, (current, _) => record = current).Write(texture, PortablePassUsage.ColorAttachment);
        });
        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(GraphFixture.Plan());

        Assert.Throws<ObjectDisposedException>(() => build!.Retain(new GraphFixture.Lease()));
        Assert.Throws<ObjectDisposedException>(() => builder!.Read(texture!, PortablePassUsage.SampledRead));
        Assert.Throws<ObjectDisposedException>(() => record!.GetTexture(texture!));
        Assert.Throws<ObjectDisposedException>(() => record!.Commands);
    }

    [Fact]
    public async Task RecordingCannotUseAnUndeclaredResource()
    {
        var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            PortablePassBuffer hidden = context.CreateBuffer("hidden", new(16, GpuBufferUsage.Storage));
            context.AddPass("output", hidden, static (record, value) => record.GetBufferRange(value))
                .Write(context.ImportTexture(output), PortablePassUsage.ColorAttachment);
        });

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());

        Assert.Contains("did not declare", error.Message);
        Assert.Empty(backend.TestQueue.Submitted);
    }

    [Fact]
    public async Task AWriteOnlyFeatureCannotReadOldOutputContents()
    {
        var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
            context.AddPass("partial", 0, static (_, _) => { }).ReadWrite(context.ImportTexture(output), PortablePassUsage.ColorAttachment));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());

        Assert.Contains("exceeds the feature declaration", error.Message);
    }

    [Fact]
    public async Task DuplicatePassNamesFailBeforeSubmission()
    {
        var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            context.AddPass("same", 0, static (_, _) => { });
            context.AddPass("same", 0, static (_, _) => { });
        });

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => runtime.SubmitAsync(GraphFixture.Plan()).AsTask());

        Assert.Equal("name", error.ParamName);
    }
}

internal static class GraphFixture
{
    internal static async Task<PortableRenderRuntime> Runtime(ManagerTestBackend backend,
        Action<PortablePassBuildContext, GpuRenderGraphTexture> build)
    {
        var registry = new PortableRenderPassRegistry(); registry.Register(Contract.Instance, _ => new Pass(build));
        return (PortableRenderRuntime)await new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), registry).CreateAsync(new());
    }
    internal static GpuRenderGraphPlan Plan(bool preserved = false, GpuGraphTextureRef? external = null)
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture output = external is null ? graph.CreateTexture("output", new(4, 4, GpuFormat.Rgba8Unorm)) : graph.ImportTexture("output", external);
        graph.AddPass("feature", new Contract(preserved), output);
        if (external is null) { graph.ExportTexture(output); } else { graph.MarkOutput(output); }
        return graph.Compile();
    }
    private sealed class Contract(bool preserved = false) : IGpuRenderPassContract<GpuRenderGraphTexture, GpuRenderGraphTexture>
    {
        internal static readonly Contract Instance = new();
        public string Id => "test.feature";
        public int Version => 1;
        public GpuRenderGraphTexture Snapshot(GpuRenderGraphTexture value) => value;
        public GpuRenderGraphTexture Declare(GpuPassDeclarationContext context, GpuRenderGraphTexture value)
        { context.Write(value); if (preserved) { context.Preserve(); } return value; }
    }
    private sealed class Pass(Action<PortablePassBuildContext, GpuRenderGraphTexture> build) : IPortableRenderPass<GpuRenderGraphTexture, GpuRenderGraphTexture>
    {
        public ValueTask BuildAsync(PortablePassBuildContext context, GpuRenderGraphTexture request, GpuRenderGraphTexture result, CancellationToken cancellationToken)
        { build(context, request); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    internal sealed class Lease(Action? release = null) : IDisposable
    {
        internal int Returns { get; private set; }
        public void Dispose() { Returns++; release?.Invoke(); }
    }
}
