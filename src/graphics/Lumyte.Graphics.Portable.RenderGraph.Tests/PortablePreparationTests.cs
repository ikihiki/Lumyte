using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

namespace Lumyte.Graphics.Portable.RenderGraph.Tests;

public sealed class PortablePreparationTests
{
    [Fact]
    public async Task AnImportedTextureOutputWithoutAnyPassRetainsItsGpuUse()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, static (_, _) => Assert.Fail("No feature should be built."));
        var description = new GpuTextureDescription(GpuTextureDimension.Texture2D, 4, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled);
        GpuTextureHandle raw = backend.CreateTexture(description);
        var lease = new GraphFixture.Lease(() => backend.DestroyTexture(raw));
        using PortableGraphResourceImport<GpuGraphTextureRef> import = runtime.Resources.ImportTexture(raw, description, lease);
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture texture = graph.ImportTexture("existing", import.Reference);
        graph.MarkOutput(texture);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        import.Dispose(); execution.Dispose();
        runtime.Resources.Collect();

        Assert.Equal(0, lease.Returns);
        backend.TestQueue.Complete(0);
        await execution.WaitForCompletionAsync(); runtime.Resources.Collect();
        Assert.Equal(1, lease.Returns);
    }

    [Fact]
    public async Task AnImportedBufferOutputWithoutAnyPassRetainsItsGpuUse()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, static (_, _) => Assert.Fail("No feature should be built."));
        var description = new GpuBufferDescription(16, GpuBufferUsage.Storage);
        GpuBufferHandle raw = backend.CreateBuffer(description);
        var lease = new GraphFixture.Lease(() => backend.DestroyBuffer(raw));
        using PortableGraphResourceImport<GpuGraphBufferRef> import = runtime.Resources.ImportBuffer(raw, description, lease);
        var graph = new GpuRenderGraph();
        GpuRenderGraphBuffer buffer = graph.ImportBuffer("existing", import.Reference);
        graph.MarkOutput(buffer);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        import.Dispose(); execution.Dispose();
        runtime.Resources.Collect();

        Assert.Equal(0, lease.Returns);
        backend.TestQueue.Complete(0);
        await execution.WaitForCompletionAsync(); runtime.Resources.Collect();
        Assert.Equal(1, lease.Returns);
    }

    [Fact]
    public async Task HostExternalImportExposesACommonReferenceAndRetainsItForSubmittedWork()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
            context.AddPass("output", 0, static (_, _) => { }).Write(context.ImportTexture(output), PortablePassUsage.ColorAttachment));
        var description = new GpuTextureDescription(GpuTextureDimension.Texture2D, 4, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment);
        GpuTextureHandle raw = backend.CreateTexture(description); var lease = new GraphFixture.Lease(() => backend.DestroyTexture(raw));
        using PortableGraphResourceImport<GpuGraphTextureRef> import = runtime.Resources.ImportTexture(raw, description, lease);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(GraphFixture.Plan(external: import.Reference));
        import.Dispose(); execution.Dispose(); runtime.Resources.Collect();
        Assert.Equal(0, lease.Returns);
        backend.TestQueue.Complete(0); await execution.WaitForCompletionAsync(); runtime.Resources.Collect();

        Assert.Equal(1, lease.Returns);
    }

    [Fact]
    public async Task RepeatedTemplatesReuseScheduleAndLifetimePreparation()
    {
        var template = new PortablePassTemplate<GpuRenderGraphTexture>((context, output) =>
            context.AddPass("output", 0, static (_, _) => { }).Write(context.ImportTexture(output), PortablePassUsage.ColorAttachment));
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(new(), (context, output) => context.Instantiate(template, output));
        GpuRenderGraphPlan plan = GraphFixture.Plan();
        using GpuRenderGraphExecution first = await runtime.SubmitAsync(plan);
        await first.WaitForCompletionAsync();

        using GpuRenderGraphExecution second = await runtime.SubmitAsync(plan);
        await second.WaitForCompletionAsync();

        Assert.Equal(new PortableRenderPreparationStatistics(1, 1), runtime.PreparationStatistics);
    }

    [Fact]
    public async Task PreparationCacheEvictsTheLeastRecentlyUsedValue()
    {
        using var cache = new PortablePassPreparationCache<int, object>(2);
        int preparations = 0;
        ValueTask<object> Create(int _, CancellationToken cancellation) { preparations++; return ValueTask.FromResult(new object()); }
        object first = await cache.GetOrCreateAsync(1, Create);
        object second = await cache.GetOrCreateAsync(2, Create);
        Assert.Same(first, await cache.GetOrCreateAsync(1, Create));
        await cache.GetOrCreateAsync(3, Create);

        object recreated = await cache.GetOrCreateAsync(2, Create);

        Assert.NotSame(second, recreated);
        Assert.Equal((2, 4), (cache.Count, preparations));
        cache.Clear(); Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task FailedPreparationCanBeRetried()
    {
        using var cache = new PortablePassPreparationCache<int, string>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync(1,
            static (_, _) => ValueTask.FromException<string>(new InvalidOperationException("prepare"))).AsTask());

        string value = await cache.GetOrCreateAsync(1, static (_, _) => ValueTask.FromResult("ready"));

        Assert.Equal("ready", value); Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task RawExternalLifetimeIsRetainedThroughGpuCompletion()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        var description = new GpuBufferDescription(16, GpuBufferUsage.Storage);
        GpuBufferHandle raw = backend.CreateBuffer(description); var lease = new GraphFixture.Lease(() => backend.DestroyBuffer(raw));
        GpuBufferHandle? observed = null;
        await using PortableRenderRuntime runtime = await GraphFixture.Runtime(backend, (context, output) =>
        {
            PortablePassBuffer imported = context.ImportBuffer("external", raw, description, lease, precedingSubmission: null);
            context.AddPass("read", imported, (record, value) => observed = record.GetBufferRange(value).Buffer)
                .Read(imported, PortablePassUsage.StorageRead).Write(context.ImportTexture(output), PortablePassUsage.ColorAttachment);
        });

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(GraphFixture.Plan());
        execution.Dispose(); runtime.Resources.Collect();
        Assert.Same(raw, observed); Assert.Equal(0, lease.Returns);
        backend.TestQueue.Complete(0); await execution.WaitForCompletionAsync(); runtime.Resources.Collect();

        Assert.Equal(1, lease.Returns); Assert.Contains(raw, backend.Destroyed);
    }
}
