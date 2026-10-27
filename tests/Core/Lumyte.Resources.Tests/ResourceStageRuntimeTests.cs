using System.Collections.Concurrent;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies per-stage execution selection and the lifetime of selected runtime services.</summary>
public sealed class ResourceStageRuntimeTests
{
    /// <summary>A parent uses the default dispatcher while its child uses the selected dispatcher and context.</summary>
    /// <returns>Completion of dependency construction and both dispatchers' cleanup.</returns>
    [Fact]
    public async Task ParentAndChildUseTheirOwnSelectedRuntimeAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var fallback = new RecordingRuntime(execution.Resources, "default");
        var selected = new RecordingRuntime(execution.Resources, "selected");
        await using ResourceManager manager = new(
            "parent-child",
            new UnusedSource(),
            [new ParentStage(), new LeafStage()],
            memory,
            fallback,
            stageRuntimes: [new(typeof(LeafValue), selected)]);
        using ResourceReference<ParentValue> reference = await manager.RetainAsync(new ResourceKey<ParentValue>("asset"));
        using ResourceLease<ParentValue> lease = reference.Pin();
        ParentValue parent = lease.Value;
        Assert.Same(fallback, parent.Runtime);
        Assert.Same(selected, parent.Child.Runtime);
        Assert.Equal(1, fallback.Builds);
        Assert.Equal(1, selected.Builds);

        lease.Dispose();
        reference.Dispose();
        Assert.Equal(2, await manager.TrimAsync());
        Assert.Same(parent, Assert.Single(fallback.Released));
        Assert.Same(parent.Child, Assert.Single(selected.Released));
    }

    /// <summary>Bindings are frozen, and automatic runtime invalidation rebuilds and releases through the original dispatcher.</summary>
    /// <returns>Completion of two device generations and their exact cleanup routing.</returns>
    [Fact]
    public async Task FrozenSelectionTracksDeviceChangesAndRetainsReleaseOwnershipAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var selected = new RecordingRuntime(execution.Resources, "device");
        var replacement = new RecordingRuntime(execution.Resources, "replacement");
        List<ResourceStageRuntime> bindings = [new(typeof(LeafValue), selected)];
        await using ResourceManager manager = new(
            "frozen",
            new UnusedSource(),
            [new LeafStage()],
            memory,
            execution.Resources,
            stageRuntimes: bindings);
        using ResourceReference<LeafValue> reference = await manager.RetainAsync(new ResourceKey<LeafValue>("asset"));
        using ResourceLease<LeafValue> oldLease = reference.Pin();
        LeafValue old = oldLease.Value;
        Assert.Equal(1UL, old.Version);
        Assert.Equal(1, selected.Subscriptions);

        bindings[0] = new(typeof(LeafValue), replacement);
        selected.InvalidateAndAdvance();
        Assert.False(oldLease.IsUsable);
        Assert.False(reference.IsAvailable);
        ResourceReloadResult result = await manager.RebuildRuntimeAsync([selected.Id]);
        Assert.Equal(ResourceReloadStatus.Published, result.Status);
        using ResourceLease<LeafValue> currentLease = reference.Pin();
        LeafValue current = currentLease.Value;
        Assert.Same(selected, current.Runtime);
        Assert.Equal(2UL, current.Version);
        Assert.Equal(2, selected.Builds);
        Assert.Equal(0, replacement.Builds);
        Assert.False(oldLease.IsUsable);

        oldLease.Dispose();
        currentLease.Dispose();
        reference.Dispose();
        Assert.Equal(2, await manager.TrimAsync());
        Assert.Equal(2, selected.Released.Count);
        Assert.Contains(old, selected.Released);
        Assert.Contains(current, selected.Released);
        Assert.Empty(replacement.Released);
        await manager.DisposeAsync();
        Assert.Equal(0, selected.Subscriptions);
    }

    /// <summary>The same provider selected and explicitly registered is subscribed only once.</summary>
    /// <returns>Completion of construction and provider subscription cleanup.</returns>
    [Fact]
    public async Task ExplicitAndSelectedSameProviderAreRegisteredOnceAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var selected = new RecordingRuntime(execution.Resources, "device");
        await using ResourceManager manager = new(
            "deduplicated",
            new UnusedSource(),
            [new LeafStage()],
            memory,
            execution.Resources,
            runtimeDependencies: [selected],
            stageRuntimes: [new(typeof(LeafValue), selected)]);
        Assert.Equal(1, selected.Subscriptions);
        using ResourceReference<LeafValue> reference = await manager.RetainAsync(new ResourceKey<LeafValue>("asset"));
        using ResourceLease<LeafValue> lease = reference.Pin();
        Assert.Same(selected, lease.Value.Runtime);
    }

    /// <summary>A provider ID cannot refer to different explicit and selected service instances.</summary>
    /// <returns>Completion of the borrowed memory and execution services.</returns>
    [Fact]
    public async Task DifferentProvidersWithTheSameIdAreRejectedAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        object sharedIdentity = new();
        var selected = new RecordingRuntime(execution.Resources, "device", sharedIdentity);
        var other = new RecordingRuntime(execution.Resources, "device", sharedIdentity);
        Assert.Throws<ArgumentException>(() => new ResourceManager(
            "conflicting",
            new UnusedSource(),
            [new LeafStage()],
            memory,
            execution.Resources,
            runtimeDependencies: [other],
            stageRuntimes: [new(typeof(LeafValue), selected)]));
        Assert.Equal(0, selected.Subscriptions);
        Assert.Equal(0, other.Subscriptions);
    }

    /// <summary>Bindings must be initialized and select a known stage exactly once.</summary>
    /// <param name="configuration">The invalid configuration: unknown output, duplicate output or default binding.</param>
    /// <returns>Completion of the borrowed memory and execution services.</returns>
    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("default")]
    public async Task InvalidStageBindingsAreRejectedAsync(string configuration)
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        ResourceStageRuntime[] bindings = configuration switch
        {
            "duplicate" => [new(typeof(LeafValue), execution.Resources), new(typeof(LeafValue), execution.Resources)],
            "default" => [default],
            _ => [new(typeof(ParentValue), execution.Resources)],
        };
        Assert.Throws<ArgumentException>(() => new ResourceManager(
            "invalid",
            new UnusedSource(),
            [new LeafStage()],
            memory,
            execution.Resources,
            stageRuntimes: bindings));
    }

    /// <summary>One manager resolves typed and factory environments once and preserves its shared default.</summary>
    /// <returns>Completion of the shared manager and its execution host.</returns>
    [Fact]
    public async Task DependencyInjectionSelectsPerStageWithoutReplacingTheDefaultAsync()
    {
        ServiceCollection services = new();
        services.AddMemoryManagement();
        List<RecordingRuntime> typedInstances = [];
        services.AddSingleton(provider =>
        {
            var runtime = new RecordingRuntime(provider.GetRequiredService<ResourceRuntime>(), "typed");
            typedInstances.Add(runtime);
            return runtime;
        });
        services.AddKeyedSingleton<RecordingRuntime>("factory", (provider, _) => new RecordingRuntime(provider.GetRequiredService<ResourceRuntime>(), "factory"));
        int factoryCalls = 0;
        services.AddResources(builder => builder.UseSource<UnusedSource>()
            .AddSourcePipeline<LeafValue, LeafStage>()
            .AddSourcePipeline<ParentValue, ParentStage>()
            .AddSourcePipeline<DefaultValue, DefaultStage>()
            .UseStageRuntime<LeafValue, RecordingRuntime>()
            .UseStageRuntime<ParentValue>(provider =>
            {
                factoryCalls++;
                return provider.GetRequiredKeyedService<RecordingRuntime>("factory");
            }));
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using ResourceExecutionHost execution = new(provider);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.Empty(typedInstances);
        Assert.Equal(0, factoryCalls);
        RecordingRuntime factory = provider.GetRequiredKeyedService<RecordingRuntime>("factory");
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        RecordingRuntime typed = Assert.Single(typedInstances);
        Assert.Same(manager, scope.ServiceProvider.GetRequiredService<ResourceManager>());
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using (ResourceReference<ParentValue> reference = await manager.RetainAsync(new ResourceKey<ParentValue>("asset")))
            using (ResourceLease<ParentValue> lease = reference.Pin())
            using (ResourceReference<DefaultValue> defaultReference = await manager.RetainAsync(new ResourceKey<DefaultValue>("asset")))
            using (ResourceLease<DefaultValue> defaultLease = defaultReference.Pin())
            {
                Assert.Same(factory, lease.Value.Runtime);
                Assert.Same(typed, lease.Value.Child.Runtime);
                Assert.Same(execution.Resources, defaultLease.Value.Runtime);
            }

            Assert.Equal(3, await manager.TrimAsync());
        }

        Assert.Equal(1, factoryCalls);
        Assert.Equal(2, typed.Builds);
        Assert.Equal(2, factory.Builds);
        Assert.Equal(2, factory.Released.Count);
        Assert.Same(execution.Resources, provider.GetRequiredService<IResourceRuntime>());
        await manager.DisposeAsync();
    }

    /// <summary>A singleton manager rejects a selected scoped environment when DI scope validation is enabled.</summary>
    /// <returns>Completion of the rejected provider configuration.</returns>
    [Fact]
    public async Task DependencyInjectionRejectsScopedEnvironmentAsync()
    {
        ServiceCollection services = new();
        services.AddMemoryManagement();
        services.AddScoped(provider => new RecordingRuntime(provider.GetRequiredService<ResourceRuntime>(), "scoped"));
        services.AddResources(builder => builder.UseSource<UnusedSource>()
            .AddSourcePipeline<LeafValue, LeafStage>().UseStageRuntime<LeafValue, RecordingRuntime>());
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ResourceManager>());
    }

    /// <summary>The registration builder rejects unknown and duplicate stage runtime selections.</summary>
    [Fact]
    public void DependencyInjectionRejectsUnknownAndDuplicateSelections()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddResources(builder => builder
            .UseSource<UnusedSource>().AddSourcePipeline<LeafValue, LeafStage>()
            .UseStageRuntime<ParentValue, ResourceRuntime>()));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddResources(builder => builder
            .UseSource<UnusedSource>().AddSourcePipeline<LeafValue, LeafStage>()
            .UseStageRuntime<LeafValue, ResourceRuntime>().UseStageRuntime<LeafValue, ResourceRuntime>()));
    }

    private sealed class RecordingRuntime(IResourceRuntime inner, string id, object? identity = null) : IResourceRuntime, IResourceExecutor, IResourceRuntimeDependency
    {
        private EventHandler? _changed;
        private int _builds;

        public event EventHandler? Changed
        {
            add
            {
                _changed += value;
                Subscriptions++;
            }

            remove
            {
                _changed -= value;
                Subscriptions--;
            }
        }

        public string Id => id;

        public object Identity => identity ?? this;

        public ResourceRuntimeSnapshot Snapshot { get; private set; } = new(1, true, 0);

        public IReadOnlyList<IResourceRuntimeDependency> Dependencies => [this];

        public IResourceExecutor Executor => this;

        public bool ShouldYield => inner.Executor.ShouldYield;

        internal int Builds => Volatile.Read(ref _builds);

        internal int Subscriptions { get; private set; }

        internal ConcurrentQueue<object> Released { get; } = new();

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Assert.Same(this, context.Runtime);
            Interlocked.Increment(ref _builds);
            return inner.Executor.BuildAsync(context, cancellationToken);
        }

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
        {
            Released.Enqueue(resource);
            return inner.Executor.ReleaseAsync(stage, runtime, resource);
        }

        internal void InvalidateAndAdvance()
        {
            Snapshot = new(Snapshot.Version + 1, true, Snapshot.Version);
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record LeafValue(IResourceRuntime Runtime, ulong Version);

    private sealed record ParentValue(IResourceRuntime Runtime, LeafValue Child);

    private sealed class LeafStage : IResourceSourceStage<LeafValue>
    {
        public ValueTask<LeafValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ulong version = 0;
            if (context.Runtime is IResourceRuntimeDependency dependency)
            {
                version = dependency.Snapshot.Version;
                context.TrackRuntimeDependency(dependency.Id, version, providerIdentity: dependency.Identity);
            }

            return ValueTask.FromResult(new LeafValue(context.Runtime, version));
        }

        public ValueTask ReleaseAsync(LeafValue resource) => ValueTask.CompletedTask;
    }

    private sealed record DefaultValue(IResourceRuntime Runtime);

    private sealed class DefaultStage : IResourceSourceStage<DefaultValue>
    {
        public ValueTask<DefaultValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new DefaultValue(context.Runtime));

        public ValueTask ReleaseAsync(DefaultValue resource) => ValueTask.CompletedTask;
    }

    private sealed class ParentStage : IResourceSourceStage<ParentValue>
    {
        public async ValueTask<ParentValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            LeafValue child = await context.RequireAsync(new ResourceKey<LeafValue>(context.Address), cancellationToken: cancellationToken);
            return new(context.Runtime, child);
        }

        public ValueTask ReleaseAsync(ParentValue resource) => ValueTask.CompletedTask;
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }
}
