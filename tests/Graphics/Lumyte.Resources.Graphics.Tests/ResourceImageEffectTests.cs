using System.Threading.Channels;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Graphics.ImageEffects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Verifies managed effects retain a coherent shader pair through preparation and GPU use.</summary>
public sealed class ResourceImageEffectTests
{
    /// <summary>Embedded shaders need no source entries and are shared until every compiled effect finishes.</summary>
    /// <returns>Completion of shared-module and submitted-plan lifetime checks.</returns>
    [Fact]
    public async Task EmbeddedEffectsShareShadersAndRetainThemUntilGpuCompletionAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync();
        TestDevice device = Assert.Single(state.Devices);
        Assert.Equal(2, device.Shaders.Count);
        var graph = new RenderGraphBuilder(device);
        using ResourceRenderGraphImports imports = graph.ImportResources(manager);
        EffectImage source = Source(graph);
        var first = new FixedResource();
        var second = new FixedResource();
        var inspected = new List<ImageEffectShaders>();
        EffectImage intermediate = imports.AppendEffect(reference, new OwningEffect(first, inspected), source);
        EffectImage output = imports.AppendEffects(reference, ImageEffectGraph.Create(new OwningEffect(second, inspected), ImageEffectGraph.Source()), intermediate);
        Assert.Equal(source, output);
        Assert.Equal(2, device.Shaders.Count);
        Assert.Same(inspected[0], inspected[1]);
        Assert.Equal(0, provider.GetRequiredService<UnusedSource>().OpenCount);
        using CompiledRenderGraph plan = imports.Compile();
        imports.Dispose();
        reference.Dispose();
        using var commands = new TestGraphCommands();
        plan.Record(commands);
        commands.Finish();
        commands.Submit();

        Assert.Throws<InvalidOperationException>(() => plan.Dispose());
        Assert.Equal(0, await manager.TrimAsync());
        Assert.Equal(0, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);
        Assert.All(device.Shaders, shader => Assert.False(shader.Disposed));

        commands.Complete();
        plan.Dispose();
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.All(device.Shaders, shader => Assert.False(shader.Disposed));
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>One package publication replaces both custom shader inputs while snapshots retain the old pair.</summary>
    /// <returns>Completion of automatic reload and old-frame publication checks.</returns>
    [Fact]
    public async Task CustomShaderInputsReloadAsOnePackageAndPreserveCapturedFramesAsync()
    {
        var state = new GraphicsTestState();
        var source = new MutableShaderSource();
        await using ServiceProvider provider = CreateProvider(state, source);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync();
        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        using ResourceLease<ImageEffectShaderResource> old = reference.Acquire(snapshot);
        AssertRevision(old.Value.Shaders, 1);
        var reloaded = new TaskCompletionSource<ResourceReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                reloaded.TrySetResult(args.Result);
            }
        };
        source.Change(2);
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using ResourceLease<ImageEffectShaderResource> current = reference.Acquire();
        Assert.NotSame(old.Value, current.Value);
        AssertRevision(current.Value.Shaders, 2);
        AssertRevision(old.Value.Shaders, 1);
        TestDevice device = Assert.Single(state.Devices);
        Assert.Equal(4, device.Shaders.Count);
        var oldGraph = new RenderGraphBuilder(device);
        using ResourceRenderGraphImports oldImports = oldGraph.ImportResources(manager);
        var oldInspected = new List<ImageEffectShaders>();
        _ = oldImports.AppendEffects(reference, ImageEffectGraph.Create(new OwningEffect(new(), oldInspected), ImageEffectGraph.Source()), Source(oldGraph), snapshot);
        using CompiledRenderGraph oldPlan = oldImports.Compile();
        Assert.Same(old.Value.Shaders, Assert.Single(oldInspected));
        snapshot.Dispose();
        old.Dispose();
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.False(shader.Disposed));

        oldPlan.Dispose();
        await manager.TrimAsync();
        Assert.All(device.Shaders.Take(2), shader => Assert.Equal(1, shader.DisposeCount));
        Assert.All(device.Shaders.Skip(2), shader => Assert.False(shader.Disposed));
    }

    /// <summary>Device recovery replaces native modules while preserving the CPU shader pair.</summary>
    /// <returns>Completion of invalidation, recreation and old-epoch release checks.</returns>
    [Fact]
    public async Task DeviceRecoveryReusesArtifactsAndRecreatesBothModulesAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync();
        using ResourceLease<ImageEffectShaderResource> old = reference.Acquire();
        ImageEffectShaderResource previous = old.Value;
        state.Devices[0].Lose();
        Assert.False(old.IsUsable);
        Assert.False(reference.IsAvailable);
        await devices.RecoverAsync();
        ResourceReloadResult result = await manager.RebuildRuntimeAsync([provider.GetRequiredService<GraphicsResourceRuntime>().Id]);
        Assert.Equal(ResourceReloadStatus.Published, result.Status);
        using ResourceLease<ImageEffectShaderResource> current = reference.Acquire();
        Assert.Same(previous.Shaders.Fullscreen, current.Value.Shaders.Fullscreen);
        Assert.Same(previous.Shaders.Effects, current.Value.Shaders.Effects);
        Assert.NotSame(previous.Device, current.Value.Device);
        Assert.True(current.Value.Epoch > previous.Epoch);
        Assert.All(state.Devices, device => Assert.Equal(2, device.Shaders.Count));
        Assert.All(state.Devices[0].Shaders, shader => Assert.False(shader.Disposed));
        old.Dispose();
        await manager.TrimAsync();
        Assert.All(state.Devices[0].Shaders, shader => Assert.Equal(1, shader.DisposeCount));
        Assert.All(state.Devices[1].Shaders, shader => Assert.False(shader.Disposed));
        Assert.Equal(1, await devices.RetireAsync());
    }

    /// <summary>Failed fixed-resource cleanup retains both shader modules until the same plan succeeds on retry.</summary>
    /// <returns>Completion of grouped lifetime cleanup retry checks.</returns>
    [Fact]
    public async Task FailedEffectCleanupKeepsShaderLeaseUntilRetryAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync();
        TestDevice device = Assert.Single(state.Devices);
        var graph = new RenderGraphBuilder(device);
        using ResourceRenderGraphImports imports = graph.ImportResources(manager);
        var resource = new FixedResource(failOnce: true);
        _ = imports.AppendEffect(reference, new OwningEffect(resource, []), Source(graph));
        using CompiledRenderGraph plan = imports.Compile();
        reference.Dispose();

        Assert.Throws<AggregateException>(() => plan.Dispose());
        Assert.Equal(0, await manager.TrimAsync());
        Assert.All(device.Shaders, shader => Assert.False(shader.Disposed));
        Assert.Equal(1, resource.DisposeCount);
        plan.Dispose();
        Assert.Equal(2, resource.DisposeCount);
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>Failed preparation preserves cleanup ownership and rejects compilation until the scope is discarded.</summary>
    /// <param name="useGraph">Whether to append a composed effect graph instead of one effect.</param>
    /// <returns>Completion of preparation failure and subsequent cleanup retry checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPreparationRetainsCleanupUntilScopeDisposalSucceedsAsync(bool useGraph)
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync();
        TestDevice device = Assert.Single(state.Devices);
        var graph = new RenderGraphBuilder(device);
        using ResourceRenderGraphImports imports = graph.ImportResources(manager);
        EffectImage source = Source(graph);
        var resource = new FixedResource(failOnce: true);
        var failure = new InvalidOperationException("Injected effect preparation failure.");
        var effect = new FailingEffect(resource, failure);
        Exception observed = Assert.Throws<InvalidOperationException>(() =>
        {
            if (useGraph)
            {
                _ = imports.AppendEffects(reference, ImageEffectGraph.Create(effect, ImageEffectGraph.Source()), source);
            }
            else
            {
                _ = imports.AppendEffect(reference, effect, source);
            }
        });
        Assert.Same(failure, observed);
        reference.Dispose();
        Assert.Equal(0, resource.DisposeCount);
        Assert.Equal(0, await manager.TrimAsync());
        Assert.Throws<ObjectDisposedException>(() => imports.Compile());
        Assert.Throws<AggregateException>(() => imports.Dispose());
        Assert.Equal(1, resource.DisposeCount);
        Assert.Equal(0, await manager.TrimAsync());
        Assert.All(device.Shaders, shader => Assert.False(shader.Disposed));

        imports.Dispose();
        Assert.Equal(2, resource.DisposeCount);
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>Failure creating the second shader returns the first module and its device ownership.</summary>
    /// <returns>Completion of partially-created package cleanup.</returns>
    [Fact]
    public async Task PartialShaderCreationReleasesTheFirstModuleAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        TestDevice device = Assert.Single(state.Devices);
        device.CreateShaderFailOnCall = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.AcquireImageEffectShadersAsync().AsTask());
        await manager.TrimAsync();
        Assert.Equal(1, Assert.Single(device.Shaders).DisposeCount);
        device.CreateShaderFailOnCall = 0;
        using ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync();
        Assert.Equal(3, device.Shaders.Count);
        reference.Dispose();
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    private static ServiceProvider CreateProvider(GraphicsTestState state, MutableShaderSource? source = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        if (source is null)
        {
            services.AddResources(builder => builder.UseSource<UnusedSource>().AddImageEffects());
        }
        else
        {
            services.AddSingleton(source);
            services.AddSingleton(new ImageEffectShaderPackage(new("fullscreen"), new("effects")));
            services.AddResources(builder => builder.UseSource<MutableShaderSource>()
                .AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>()
                .AddImageEffects()
                .ConfigureHotReload(new() { DebounceDelay = TimeSpan.Zero, MaximumBatchDelay = TimeSpan.FromMilliseconds(10) }));
        }

        services.AddResourceGraphics();
        return services.BuildServiceProvider();
    }

    private static EffectImage Source(RenderGraphBuilder graph) => new(
        graph.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment }),
        new(0, 0, 4, 4),
        EffectColorSemantic.DisplayLinear);

    private static void AssertRevision(ImageEffectShaders shaders, byte revision)
    {
        Assert.Equal(revision, Assert.Single(shaders.Fullscreen.GetTarget(ShaderTarget.Wgsl).Code));
        Assert.Equal(revision, Assert.Single(shaders.Effects.GetTarget(ShaderTarget.Wgsl).Code));
    }

    private sealed record OwningEffect(FixedResource Resource, List<ImageEffectShaders> Inspected) : ImageEffectDefinition
    {
        public override int InputCount => 1;

        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;

        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
        {
            Inspected.Add(Assert.IsType<ImageEffectShaders>(context.Shaders));
            context.Own(Resource);
            return inputs[0];
        }
    }

    private sealed class FixedResource(bool failOnce = false) : IDisposable
    {
        internal int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (failOnce && DisposeCount == 1)
            {
                throw new InvalidOperationException("Injected effect cleanup failure.");
            }
        }
    }

    private sealed record FailingEffect(FixedResource Resource, Exception Failure) : ImageEffectDefinition
    {
        public override int InputCount => 1;

        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;

        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
        {
            context.Own(Resource);
            throw Failure;
        }
    }

    private sealed class UnusedSource : IResourceSource
    {
        internal int OpenCount { get; private set; }

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            throw new InvalidOperationException("Embedded effects must not read the application source.");
        }

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Embedded effects must not query the application source.");
    }

    private sealed class MutableShaderSource : IResourceSource, IResourceChangeSource
    {
        private readonly Channel<ResourceSourceChange> _changes = Channel.CreateUnbounded<ResourceSourceChange>();
        private int _revision = 1;

        public IResourceChangeSource Changes => this;

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => new(new ShaderSnapshot(id, Volatile.Read(ref _revision)));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => new(Volatile.Read(ref _revision).ToString(System.Globalization.CultureInfo.InvariantCulture));

        public IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default)
            => _changes.Reader.ReadAllAsync(cancellationToken);

        internal void Change(int revision)
        {
            Volatile.Write(ref _revision, revision);
            Assert.True(_changes.Writer.TryWrite(ResourceSourceChange.Rescan));
        }
    }

    private sealed class ShaderSnapshot(ResourceId id, int revision) : ResourceSourceSnapshot
    {
        private readonly byte[] _binary = ShaderArtifact.PackTarget(
            ShaderTarget.Wgsl, id.Value == "fullscreen" ? ShaderStage.Vertex : ShaderStage.Fragment, "main", "test", [(byte)revision], "{}").GetBinary();

        public override ResourceId Id => id;

        public override string Revision => revision.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public override long? Length => _binary.Length;

        public override bool CanReadRange => false;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => new(new MemoryStream(_binary, writable: false));

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
