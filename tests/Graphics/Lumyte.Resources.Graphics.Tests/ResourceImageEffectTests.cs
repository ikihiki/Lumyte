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
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder().AddResources(manager).AddImageEffects(manager);
        TestDevice device = Assert.Single(state.Devices);
        Assert.Empty(device.Shaders);
        Assert.Equal(0, provider.GetRequiredService<UnusedSource>().OpenCount);
        using RenderGraphExtensions extensions = await builder.BuildAsync();
        Assert.Equal(2, device.Shaders.Count);
        using var graph = new RenderGraphBuilder(device, extensions: extensions);
        EffectImage source = Source(graph);
        var first = new FixedResource();
        var second = new FixedResource();
        var inspected = new List<ImageEffectShaders>();
        EffectImage intermediate = graph.AppendEffect(new OwningEffect(first, inspected), source);
        EffectImage output = graph.AppendEffects(ImageEffectGraph.Create(new OwningEffect(second, inspected), ImageEffectGraph.Source()), intermediate);
        Assert.Equal(source, output);
        Assert.Equal(2, device.Shaders.Count);
        Assert.Same(inspected[0], inspected[1]);
        Assert.Equal(0, provider.GetRequiredService<UnusedSource>().OpenCount);
        using CompiledRenderGraph plan = graph.Compile();
        graph.Dispose();
        extensions.Dispose();
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

    /// <summary>Nested layer callbacks and direct effects resolve the same resource and effects extensions.</summary>
    /// <returns>Completion of shared state and parent-plan ownership checks.</returns>
    [Fact]
    public async Task LayerAndEffectsExtensionsShareStateAndPlanOwnershipAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        int layerCreations = 0;
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder()
            .AddResources(manager).AddImageEffects(manager).Add(graph =>
            {
                layerCreations++;
                return new LayerImports(graph);
            }).BuildAsync();
        TestDevice device = Assert.Single(state.Devices);
        using var graph = new RenderGraphBuilder(device, extensions: extensions);
        EffectImage source = Source(graph);
        LayerImports layer = graph.GetExtension<LayerImports>();
        Assert.Same(layer, graph.GetExtension<LayerImports>());
        Assert.Same(layer.Effects, graph.GetExtension<ImageEffectsGraphContext>());
        Assert.Same(layer.Resources, graph.ImportResources());
        var nested = new FixedResource();
        var direct = new FixedResource();
        var inspected = new List<ImageEffectShaders>();
        EffectImage image = layer.Append(nested, inspected, source);
        _ = graph.AppendEffects(ImageEffectGraph.Create(new OwningEffect(direct, inspected), ImageEffectGraph.Source()), image);
        Assert.Equal(1, layerCreations);
        Assert.Same(inspected[0], inspected[1]);
        Assert.Equal(2, device.Shaders.Count);
        Assert.Equal(0, nested.DisposeCount);
        using CompiledRenderGraph plan = graph.Compile();
        graph.Dispose();
        extensions.Dispose();
        Assert.Equal(0, await manager.TrimAsync());
        Assert.Equal(0, nested.DisposeCount);
        Assert.Equal(0, direct.DisposeCount);

        plan.Dispose();
        Assert.Equal(1, nested.DisposeCount);
        Assert.Equal(1, direct.DisposeCount);
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>A managed effects registration never silently falls back when resources or the device mismatch.</summary>
    /// <param name="missingResources">Whether to omit the resource extension instead of supplying a foreign device.</param>
    /// <returns>Completion of configuration rejection and retained lease cleanup.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidManagedEffectsEnvironmentRejectsBeforePreparingPassesAsync(bool missingResources)
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using var builder = new RenderGraphExtensionsBuilder();
        if (!missingResources)
        {
            builder.AddResources(manager);
        }

        using RenderGraphExtensions extensions = await builder.AddImageEffects(manager).BuildAsync();
        TestDevice device = Assert.Single(state.Devices);
        await using var foreign = new TestDevice();
        using var graph = new RenderGraphBuilder(missingResources ? device : foreign, extensions: extensions);
        var resource = new FixedResource();
        var inspected = new List<ImageEffectShaders>();
        Assert.Throws<InvalidOperationException>(() => graph.AppendEffect(new OwningEffect(resource, inspected), Source(graph)));
        Assert.Empty(inspected);
        Assert.Equal(0, resource.DisposeCount);
        Assert.False(graph.IsBuilding);
        graph.Dispose();
        extensions.Dispose();
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>One package publication replaces both custom shader inputs while snapshots retain the old pair.</summary>
    /// <param name="changeDuringValidation">Whether another edit invalidates the first replacement after its GPU modules were created.</param>
    /// <returns>Completion of automatic reload and old-frame publication checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomShaderInputsReloadAsOnePackageAndPreserveCapturedFramesAsync(bool changeDuringValidation)
    {
        var state = new GraphicsTestState();
        var source = new MutableShaderSource();
        await using ServiceProvider provider = CreateProvider(state, source);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder()
            .AddResources(manager).AddImageEffects(manager).BuildAsync();
        using ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync();
        using ResourceSnapshot snapshot = manager.CaptureSnapshot();
        using ResourceLease<ImageEffectShaderResource> old = reference.Acquire(snapshot);
        AssertRevision(old.Value.Shaders, 1);
        TestDevice device = Assert.Single(state.Devices);
        TestShader[] oldModules = LiveModules(device, old.Value.Shaders);
        Assert.Equal(2, device.Shaders.Count);
        if (changeDuringValidation)
        {
            source.BeforeRevisionCheck = () =>
            {
                if (device.Shaders.Count > oldModules.Length)
                {
                    source.BeforeRevisionCheck = null;
                    source.Change(3);
                }
            };
        }

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
        AssertRevision(current.Value.Shaders, changeDuringValidation ? (byte)3 : (byte)2);
        AssertRevision(old.Value.Shaders, 1);
        TestShader[] currentModules = LiveModules(device, current.Value.Shaders);
        TestShader[] retained = [.. oldModules, .. currentModules];
        Assert.Equal(4, retained.Distinct().Count());
        Assert.Equal(4, device.Shaders.Count(shader => !shader.Disposed));
        TestShader[] discarded = device.Shaders.Except(retained).ToArray();
        if (changeDuringValidation)
        {
            Assert.NotEmpty(discarded);
        }

        // Source rechecks may legitimately create and reject an unpublished candidate before retrying.
        Assert.All(discarded, shader => Assert.Equal(1, shader.DisposeCount));
        using var oldGraph = new RenderGraphBuilder(device, extensions: extensions);
        oldGraph.ImportResources().UseSnapshot(snapshot);
        var oldInspected = new List<ImageEffectShaders>();
        _ = oldGraph.AppendEffects(ImageEffectGraph.Create(new OwningEffect(new(), oldInspected), ImageEffectGraph.Source()), Source(oldGraph));
        using CompiledRenderGraph oldPlan = oldGraph.Compile();
        Assert.Same(old.Value.Shaders, Assert.Single(oldInspected));

        using var newGraph = new RenderGraphBuilder(device, extensions: extensions);
        var newInspected = new List<ImageEffectShaders>();
        _ = newGraph.AppendEffect(new OwningEffect(new(), newInspected), Source(newGraph));
        using CompiledRenderGraph newPlan = newGraph.Compile();
        Assert.Same(current.Value.Shaders, Assert.Single(newInspected));
        snapshot.Dispose();
        old.Dispose();
        await manager.TrimAsync();
        Assert.All(retained, shader => Assert.False(shader.Disposed));
        Assert.All(discarded, shader => Assert.Equal(1, shader.DisposeCount));

        oldPlan.Dispose();
        await manager.TrimAsync();
        Assert.All(oldModules, shader => Assert.Equal(1, shader.DisposeCount));
        Assert.All(currentModules, shader => Assert.False(shader.Disposed));
        Assert.All(discarded, shader => Assert.Equal(1, shader.DisposeCount));
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
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder()
            .AddResources(manager).AddImageEffects(manager).BuildAsync();
        using ResourceReference<ImageEffectShaderResource> reference = await manager.AcquireImageEffectShadersAsync();
        using ResourceLease<ImageEffectShaderResource> old = reference.Acquire();
        ImageEffectShaderResource previous = old.Value;
        using var oldGraph = new RenderGraphBuilder(previous.Device, extensions: extensions);
        _ = oldGraph.AppendEffect(new OwningEffect(new(), []), Source(oldGraph));
        state.Devices[0].Lose();
        Assert.False(old.IsUsable);
        Assert.False(reference.IsAvailable);
        Assert.Throws<ResourceUnavailableException>(() => oldGraph.Compile());
        await devices.RecoverAsync();
        ResourceReloadResult result = await manager.RebuildRuntimeAsync([provider.GetRequiredService<GraphicsResourceRuntime>().Id]);
        Assert.Equal(ResourceReloadStatus.Published, result.Status);
        using ResourceLease<ImageEffectShaderResource> current = reference.Acquire();
        Assert.Same(previous.Shaders.Fullscreen, current.Value.Shaders.Fullscreen);
        Assert.Same(previous.Shaders.Effects, current.Value.Shaders.Effects);
        Assert.NotSame(previous.Device, current.Value.Device);
        Assert.True(current.Value.Epoch > previous.Epoch);
        using var newGraph = new RenderGraphBuilder(current.Value.Device, extensions: extensions);
        var inspected = new List<ImageEffectShaders>();
        _ = newGraph.AppendEffect(new OwningEffect(new(), inspected), Source(newGraph));
        using CompiledRenderGraph plan = newGraph.Compile();
        Assert.Same(current.Value.Shaders, Assert.Single(inspected));
        Assert.All(state.Devices, device => Assert.Equal(2, device.Shaders.Count));
        Assert.All(state.Devices[0].Shaders, shader => Assert.False(shader.Disposed));
        oldGraph.Dispose();
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
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder()
            .AddResources(manager).AddImageEffects(manager).BuildAsync();
        TestDevice device = Assert.Single(state.Devices);
        using var graph = new RenderGraphBuilder(device, extensions: extensions);
        var resource = new FixedResource(failOnce: true);
        _ = graph.AppendEffect(new OwningEffect(resource, []), Source(graph));
        using CompiledRenderGraph plan = graph.Compile();
        extensions.Dispose();

        Assert.Throws<AggregateException>(() => plan.Dispose());
        Assert.Equal(0, await manager.TrimAsync());
        Assert.All(device.Shaders, shader => Assert.False(shader.Disposed));
        Assert.Equal(1, resource.DisposeCount);
        plan.Dispose();
        Assert.Equal(2, resource.DisposeCount);
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>Failed preparation preserves cleanup ownership and rejects compilation until the graph is discarded.</summary>
    /// <param name="useGraph">Whether to append a composed effect graph instead of one effect.</param>
    /// <returns>Completion of preparation failure and subsequent cleanup retry checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPreparationRetainsCleanupUntilGraphDisposalSucceedsAsync(bool useGraph)
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder()
            .AddResources(manager).AddImageEffects(manager).BuildAsync();
        TestDevice device = Assert.Single(state.Devices);
        using var graph = new RenderGraphBuilder(device, extensions: extensions);
        EffectImage source = Source(graph);
        var resource = new FixedResource(failOnce: true);
        var failure = new InvalidOperationException("Injected effect preparation failure.");
        var effect = new FailingEffect(resource, failure);
        Exception observed = Assert.Throws<InvalidOperationException>(() =>
        {
            if (useGraph)
            {
                _ = graph.AppendEffects(ImageEffectGraph.Create(effect, ImageEffectGraph.Source()), source);
            }
            else
            {
                _ = graph.AppendEffect(effect, source);
            }
        });
        Assert.Same(failure, observed);
        extensions.Dispose();
        Assert.Equal(0, resource.DisposeCount);
        Assert.Equal(0, await manager.TrimAsync());
        Assert.Throws<ObjectDisposedException>(() => graph.Compile());
        Assert.Throws<AggregateException>(() => graph.Dispose());
        Assert.Equal(1, resource.DisposeCount);
        Assert.Equal(0, await manager.TrimAsync());
        Assert.All(device.Shaders, shader => Assert.False(shader.Disposed));

        graph.Dispose();
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
        using RenderGraphExtensionsBuilder failedBuilder = new RenderGraphExtensionsBuilder().AddResources(manager).AddImageEffects(manager);
        TestDevice device = Assert.Single(state.Devices);
        device.CreateShaderFailOnCall = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => failedBuilder.BuildAsync().AsTask());
        await manager.TrimAsync();
        Assert.Equal(1, Assert.Single(device.Shaders).DisposeCount);
        device.CreateShaderFailOnCall = 0;
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder()
            .AddResources(manager).AddImageEffects(manager).BuildAsync();
        Assert.Equal(3, device.Shaders.Count);
        extensions.Dispose();
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>Duplicate registration and an already cancelled build do not load shader resources.</summary>
    /// <returns>Completion of deferred registration and subsequent successful build checks.</returns>
    [Fact]
    public async Task RegistrationAndCancelledBuildDoNotPreloadShadersAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder().AddResources(manager).AddImageEffects(manager);
        TestDevice device = Assert.Single(state.Devices);

        Assert.Throws<ArgumentException>(() => builder.AddImageEffects(manager));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.BuildAsync(new(canceled: true)).AsTask());
        Assert.Empty(device.Shaders);
        Assert.Equal(0, provider.GetRequiredService<UnusedSource>().OpenCount);

        using RenderGraphExtensions extensions = await builder.BuildAsync();
        Assert.Equal(2, device.Shaders.Count);
        extensions.Dispose();
        await manager.TrimAsync();
        Assert.All(device.Shaders, shader => Assert.Equal(1, shader.DisposeCount));
    }

    /// <summary>A later extension failure or cancellation releases the successfully preloaded shader reference.</summary>
    /// <param name="cancel">Whether the later extension cancels instead of failing.</param>
    /// <returns>Completion of shader ownership rollback checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedBuildReleasesPreloadedShadersAsync(bool cancel)
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await provider.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("Injected extension preparation failure.");
        using RenderGraphExtensionsBuilder builder = new RenderGraphExtensionsBuilder().AddResources(manager).AddImageEffects(manager)
            .AddAsync<LayerImports>(token =>
            {
                Assert.Equal(2, Assert.Single(state.Devices).Shaders.Count);
                if (cancel)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }

                return ValueTask.FromException<RenderGraphExtensionFactory<LayerImports>>(failure);
            });

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.BuildAsync(cancellation.Token).AsTask());
        }
        else
        {
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync().AsTask()));
        }

        TestDevice device = Assert.Single(state.Devices);
        Assert.Equal(2, device.Shaders.Count);
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
            services.UseGraphicsShaders<ImageEffectShaderResource>(new("fullscreen"), new("effects"));
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

    private static TestShader[] LiveModules(TestDevice device, ImageEffectShaders shaders)
        => [
            Assert.Single(device.Shaders, shader => !shader.Disposed && ReferenceEquals(shader.Artifact, shaders.Fullscreen)),
            Assert.Single(device.Shaders, shader => !shader.Disposed && ReferenceEquals(shader.Artifact, shaders.Effects)),
        ];

    private sealed class LayerImports(RenderGraphBuilder graph)
    {
        internal ImageEffectsGraphContext Effects { get; } = graph.GetExtension<ImageEffectsGraphContext>();

        internal ResourceRenderGraphImports Resources { get; } = graph.ImportResources();

        internal EffectImage Append(FixedResource resource, List<ImageEffectShaders> inspected, EffectImage source)
        {
            using var layer = new ImageEffectSubgraph(graph, source);
            return layer.AppendEffect(new OwningEffect(resource, inspected), layer.Source);
        }
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

        internal Action? BeforeRevisionCheck { get; set; }

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => new(new ShaderSnapshot(id, Volatile.Read(ref _revision)));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            BeforeRevisionCheck?.Invoke();
            return new(Volatile.Read(ref _revision).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

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
