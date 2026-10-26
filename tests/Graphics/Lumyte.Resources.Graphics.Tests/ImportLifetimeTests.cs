using Lumyte.Graphics.RenderGraph;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Verifies shared resource extensions and parent-graph ownership.</summary>
public sealed class ImportLifetimeTests
{
    /// <summary>Independent extensions share one borrowed import context, whose manager is fixed for the graph.</summary>
    /// <returns>Completion of manager disposal.</returns>
    [Fact]
    public async Task ExtensionsShareTheConfiguredManagerAsync()
    {
        await using ServiceProvider first = CreateProvider();
        await using ServiceProvider second = CreateProvider();
        ResourceManager manager = first.GetRequiredService<ResourceManager>();
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder()
            .AddResources(manager).Add(graph => new Consumer(graph.ImportResources())).BuildAsync();
        using var graph = new RenderGraphBuilder(extensions: extensions);
        ResourceRenderGraphImports imports = graph.ImportResources();

        Assert.Same(imports, graph.ImportResources(manager));
        Assert.Same(imports, graph.GetExtension<Consumer>().Imports);
        Assert.Same(graph, imports.Graph);
        Assert.Throws<ArgumentException>(() => graph.ImportResources(second.GetRequiredService<ResourceManager>()));

        using CompiledRenderGraph plan = graph.Compile();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => imports.AcquireAsync(new ResourceKey<CpuValue>("closed")).AsTask());
    }

    /// <summary>Explicit attachment is cached and rejects manager changes without an extension configuration.</summary>
    /// <returns>Completion of manager disposal.</returns>
    [Fact]
    public async Task ExplicitManagerAttachmentIsSharedAsync()
    {
        await using ServiceProvider first = CreateProvider();
        await using ServiceProvider second = CreateProvider();
        ResourceManager manager = first.GetRequiredService<ResourceManager>();
        using var graph = new RenderGraphBuilder();
        ResourceRenderGraphImports imports = graph.ImportResources(manager);

        Assert.Same(imports, graph.ImportResources());
        Assert.Same(imports, graph.ImportResources(manager));
        Assert.Throws<ArgumentException>(() => graph.ImportResources(second.GetRequiredService<ResourceManager>()));
        graph.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => imports.AcquireAsync(new ResourceKey<CpuValue>("closed")).AsTask());
    }

    /// <summary>Preloaded acquisitions use one selected publication and retain their leases independently of its snapshot.</summary>
    /// <returns>Completion of graph and manager ownership checks.</returns>
    [Fact]
    public async Task FrameSnapshotIsSelectedBeforeAcquisitionAndLeasesOutliveItAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using ResourceReference<CpuValue> first = await manager.AcquireAsync(new ResourceKey<CpuValue>("first"));
        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        using ResourceReference<CpuValue> later = await manager.AcquireAsync(new ResourceKey<CpuValue>("later"));
        using ResourceSnapshot newer = manager.CaptureSnapshot();
        using var graph = new RenderGraphBuilder();
        ResourceRenderGraphImports imports = graph.ImportResources(manager).UseSnapshot(snapshot);
        CpuValue value = imports.Acquire(first);

        using (ResourceLease<CpuValue> expected = first.Acquire(snapshot))
        {
            Assert.Same(expected.Value, value);
        }

        Assert.Throws<InvalidOperationException>(() => imports.Acquire(later));
        Assert.Throws<InvalidOperationException>(() => imports.UseSnapshot(newer));
        Assert.Throws<ArgumentException>(() => imports.Acquire(newer, first.Handle));
        using CompiledRenderGraph plan = graph.Compile();
        graph.Dispose();
        snapshot.Dispose();
        newer.Dispose();
        first.Dispose();
        later.Dispose();

        Assert.Equal(1, await manager.TrimAsync());
        plan.Dispose();
        Assert.Equal(1, await manager.TrimAsync());
    }

    /// <summary>A borrowed import context does not accept a reference from another manager.</summary>
    /// <returns>Completion of reference and manager cleanup.</returns>
    [Fact]
    public async Task PreloadedReferencesMustBelongToTheBoundManagerAsync()
    {
        await using ServiceProvider first = CreateProvider();
        await using ServiceProvider second = CreateProvider();
        await using ResourceExecutionHost secondExecution = await ResourceExecutionHost.StartAsync(second);
        using ResourceReference<CpuValue> reference = await second.GetRequiredService<ResourceManager>().AcquireAsync(new ResourceKey<CpuValue>("foreign"));
        using var graph = new RenderGraphBuilder();
        ResourceRenderGraphImports imports = graph.ImportResources(first.GetRequiredService<ResourceManager>());

        Assert.Throws<ArgumentException>(() => imports.Acquire(reference));
    }

    /// <summary>An acquisition finishing after graph abandonment returns its reference without leaking a lease.</summary>
    /// <returns>Completion of the pending load and resource reclamation.</returns>
    [Fact]
    public async Task PendingAcquisitionReturnsOwnershipWhenGraphClosesAsync()
    {
        var pending = new PendingBuild();
        var services = new ServiceCollection();
        services.AddSingleton(pending);
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<TestSource>().AddSourcePipeline<CpuValue, PendingStage>());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using var graph = new RenderGraphBuilder();
        Task<CpuValue> acquisition = graph.ImportResources(manager).AcquireAsync(new ResourceKey<CpuValue>("pending")).AsTask();
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        graph.Dispose();
        pending.Continue.SetResult();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => acquisition);
        Assert.Equal(1, await manager.TrimAsync());
        Assert.Equal(1, pending.Released);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GraphicsTestState>();
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<TestSource>().AddSourcePipeline<CpuValue, CpuStage>());
        return services.BuildServiceProvider();
    }

    private sealed record Consumer(ResourceRenderGraphImports Imports);

    private sealed class PendingBuild
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Released { get; set; }
    }

    private sealed class PendingStage(PendingBuild pending) : IResourceSourceStage<CpuValue>
    {
        public async ValueTask<CpuValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            pending.Started.SetResult();
            await pending.Continue.Task.WaitAsync(cancellationToken);
            return new();
        }

        public ValueTask ReleaseAsync(CpuValue resource)
        {
            pending.Released++;
            return ValueTask.CompletedTask;
        }
    }
}
