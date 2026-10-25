using System.Collections.Concurrent;
using Lumyte.Memory;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Checks the environment configuration captured by the manager.</summary>
public sealed class ResourceEnvironmentTests
{
    /// <summary>All declared services are observed once and remain tracked after registration inputs change.</summary>
    /// <param name="invalidateFirst">Whether the first or second independent service is invalidated.</param>
    /// <returns>Completion of the old and rebuilt generations and provider unsubscription.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompositeEnvironmentTracksEveryDeclaredProviderAsync(bool invalidateFirst)
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var first = new Provider("first");
        var second = new Provider("second");
        var environment = new Environment(execution.Resources.Executor, first, second);
        var stage = new Stage();
        await using ResourceManager manager = new("composite", new UnusedSource(), [stage], memory, environment);
        Assert.Equal(1, first.Subscriptions);
        Assert.Equal(1, second.Subscriptions);
        environment.Declared.Clear();

        using ResourceReference<Value> reference = await manager.AcquireAsync(new ResourceKey<Value>("asset"));
        using ResourceLease<Value> oldLease = reference.Acquire();
        Value old = oldLease.Value;
        Provider changed = invalidateFirst ? first : second;
        changed.InvalidateAndAdvance();
        Assert.False(oldLease.IsUsable);
        Assert.False(reference.IsAvailable);
        Assert.Equal(ResourceReloadStatus.Published, (await manager.RebuildRuntimeAsync([changed.Id])).Status);
        using ResourceLease<Value> currentLease = reference.Acquire();
        Value current = currentLease.Value;
        Assert.NotSame(old, current);
        Assert.Equal(invalidateFirst ? 2UL : 1UL, current.FirstVersion);
        Assert.Equal(invalidateFirst ? 1UL : 2UL, current.SecondVersion);
        Assert.False(oldLease.IsUsable);

        oldLease.Dispose();
        currentLease.Dispose();
        reference.Dispose();
        await manager.DisposeAsync();
        Assert.Equal(0, first.Subscriptions);
        Assert.Equal(0, second.Subscriptions);
        Assert.Equal(2, stage.Released.Count);
        Assert.Contains(old, stage.Released);
        Assert.Contains(current, stage.Released);
    }

    /// <summary>Changing the runtime's configuration later cannot redirect a registered environment's jobs.</summary>
    /// <returns>Completion of construction and release through the captured executor.</returns>
    [Fact]
    public async Task ExecutorSelectionIsFrozenThroughReleaseAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        var environment = new Environment(execution.Resources.Executor, new Provider("first"), new Provider("second"));
        var stage = new Stage();
        await using ResourceManager manager = new("frozen-executor", new UnusedSource(), [stage], memory, environment);
        environment.Executor = new RejectedExecutor();

        using ResourceReference<Value> reference = await manager.AcquireAsync(new ResourceKey<Value>("asset"));
        using ResourceLease<Value> lease = reference.Acquire();
        Value value = lease.Value;
        lease.Dispose();
        reference.Dispose();
        Assert.Equal(1, await manager.TrimAsync());
        Assert.Same(value, Assert.Single(stage.Released));
    }

    private sealed class Environment(IResourceExecutor executor, Provider first, Provider second) : IResourceRuntime
    {
        public IResourceExecutor Executor { get; set; } = executor;

        public IReadOnlyList<IResourceRuntimeDependency> Dependencies => Declared;

        internal List<IResourceRuntimeDependency> Declared { get; } = [first, second, first];

        internal Provider First => first;

        internal Provider Second => second;
    }

    private sealed class RejectedExecutor : IResourceExecutor
    {
        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The replaced executor must not receive work.");

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
            => throw new InvalidOperationException("The replaced executor must not receive cleanup.");
    }

    private sealed class Provider(string id) : IResourceRuntimeDependency
    {
        private EventHandler? _changed;

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

        public object Identity { get; } = new();

        public ResourceRuntimeSnapshot Snapshot { get; private set; } = new(1, true, 0);

        internal int Subscriptions { get; private set; }

        internal void InvalidateAndAdvance()
        {
            Snapshot = new(Snapshot.Version + 1, true, Snapshot.Version);
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record Value(Environment Runtime, ulong FirstVersion, ulong SecondVersion);

    private sealed class Stage : IResourceSourceStage<Value, Environment>
    {
        internal ConcurrentQueue<Value> Released { get; } = new();

        public ValueTask<Value> BuildAsync(Environment runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ulong first = runtime.First.Snapshot.Version;
            ulong second = runtime.Second.Snapshot.Version;
            context.TrackRuntimeDependency(runtime.First.Id, first, providerIdentity: runtime.First.Identity);
            context.TrackRuntimeDependency(runtime.Second.Id, second, providerIdentity: runtime.Second.Identity);
            return ValueTask.FromResult(new Value(runtime, first, second));
        }

        public ValueTask ReleaseAsync(Value resource, Environment runtime)
        {
            Assert.Same(resource.Runtime, runtime);
            Released.Enqueue(resource);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }
}
