using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies that runtime invalidation retains only owned graph provenance and retryable cleanup.</summary>
public sealed class ResourceInvalidationLifetimeTests
{
    private static readonly ResourceId _input = new("asset");
    private static readonly ResourceKey<Value> _key = new(_input);

    /// <summary>Returning the last owner before or after loss lets trim remove monitoring and recovery work.</summary>
    /// <param name="returnBeforeLoss">Whether ownership is returned before the runtime becomes unavailable.</param>
    /// <returns>Completion of invalidation, reclamation and recovery without rebuilding unused values.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnownedInvalidationIsForgottenAfterTrimAsync(bool returnBeforeLoss)
    {
        await using Fixture fixture = new();
        IResourceWatchTargets targets = await fixture.Source.Events.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceReference<Value> reference = await fixture.Manager.RetainAsync(_key);
        ResourceHandle<Value> handle = reference.Handle;
        if (returnBeforeLoss)
        {
            reference.Dispose();
        }

        fixture.Dependency.Set(new(2, false, 1));
        reference.Dispose();
        Assert.Equal(1, await fixture.Manager.TrimAsync());
        Assert.Empty(targets.Resources);
        fixture.Dependency.Set(new(3, true, 1));
        ResourceReloadResult result = await fixture.Manager.RebuildRuntimeAsync([fixture.Dependency.Id]);
        Assert.Equal(ResourceReloadStatus.Unchanged, result.Status);
        Assert.Equal(1, fixture.Stage.Builds);
        Assert.False(fixture.Manager.TryRetain(handle, out ResourceReference<Value>? unavailable));
        Assert.Null(unavailable);
    }

    /// <summary>A live logical reference keeps its input provenance after the invalidated content is reclaimed.</summary>
    /// <returns>Completion of content reclamation and recovery through the original handle.</returns>
    [Fact]
    public async Task LiveReferencePreservesRecoveryAfterContentTrimAsync()
    {
        await using Fixture fixture = new();
        IResourceWatchTargets targets = await fixture.Source.Events.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceReference<Value> reference = await fixture.Manager.RetainAsync(_key);
        ResourceHandle<Value> handle = reference.Handle;
        fixture.Dependency.Set(new(2, false, 1));
        Assert.Equal(1, await fixture.Manager.TrimAsync());
        Assert.False(reference.IsAvailable);
        Assert.Equal([_input], targets.Resources);
        fixture.Dependency.Set(new(3, true, 1));
        Assert.Equal(ResourceReloadStatus.Published, (await fixture.Manager.RebuildRuntimeAsync([fixture.Dependency.Id])).Status);
        Assert.Equal(handle, reference.Handle);
        using (ResourceLease<Value> current = reference.Pin())
        {
            Assert.Equal(3UL, current.Value.Version);
        }

        Assert.Equal(2, fixture.Stage.Builds);
        reference.Dispose();
        await fixture.Manager.TrimAsync();
        Assert.Empty(targets.Resources);
    }

    /// <summary>A retained parent preserves a child's provenance for both resident and reclaimed build dependencies.</summary>
    /// <param name="lifetime">How long the parent retains the child's content.</param>
    /// <returns>Completion of child reclamation and parent recovery.</returns>
    [Theory]
    [InlineData(ResourceDependencyLifetime.Resident)]
    [InlineData(ResourceDependencyLifetime.Build)]
    public async Task ParentDependencyPreservesRecoveryMetadataAsync(ResourceDependencyLifetime lifetime)
    {
        await using Fixture fixture = new(lifetime);
        IResourceWatchTargets targets = await fixture.Source.Events.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceReference<Parent> parent = await fixture.Manager.RetainAsync(new ResourceKey<Parent>("parent"));
        await fixture.Manager.TrimAsync();
        fixture.Dependency.Set(new(2, false, 1));
        await fixture.Manager.TrimAsync();
        Assert.Equal(lifetime == ResourceDependencyLifetime.Build, parent.IsAvailable);
        Assert.Equal([_input], targets.Resources);
        fixture.Dependency.Set(new(3, true, 1));
        Assert.Equal(ResourceReloadStatus.Published, (await fixture.Manager.RebuildRuntimeAsync([fixture.Dependency.Id])).Status);
        using (ResourceLease<Parent> current = parent.Pin())
        {
            Assert.Equal(3UL, current.Value.InputVersion);
        }

        Assert.Equal(2, fixture.Stage.Builds);
        parent.Dispose();
        await fixture.Manager.TrimAsync();
        Assert.Empty(targets.Resources);
    }

    /// <summary>Failed cleanup keeps invalidated provenance until a later trim finishes its release.</summary>
    /// <returns>Completion of failed destruction, retry and recovery without resurrecting the released value.</returns>
    [Fact]
    public async Task FailedCleanupRetainsWatchUntilRetryCompletesAsync()
    {
        await using Fixture fixture = new();
        IResourceWatchTargets targets = await fixture.Source.Events.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceReference<Value> reference = await fixture.Manager.RetainAsync(_key);
        fixture.Stage.FailNextRelease = true;
        reference.Dispose();
        fixture.Dependency.Set(new(2, false, 1));
        await Assert.ThrowsAsync<AggregateException>(async () => await fixture.Manager.TrimAsync());
        Assert.Equal([_input], targets.Resources);
        Assert.Equal(1, fixture.Stage.ReleaseAttempts);
        Assert.Equal(1, await fixture.Manager.TrimAsync());
        Assert.Empty(targets.Resources);
        Assert.Equal(2, fixture.Stage.ReleaseAttempts);
        fixture.Dependency.Set(new(3, true, 1));
        Assert.Equal(ResourceReloadStatus.Unchanged, (await fixture.Manager.RebuildRuntimeAsync([fixture.Dependency.Id])).Status);
        Assert.Equal(1, fixture.Stage.Builds);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Lumyte.Memory.MemoryManager _memory = new();
        private readonly ResourceExecutionHost _execution = new();

        internal Fixture(ResourceDependencyLifetime parentLifetime = ResourceDependencyLifetime.Resident)
        {
            Stage = new(Dependency);
            Manager = new("invalidation-lifetime", Source, [Stage, new ParentStage(parentLifetime)], _memory, _execution.Resources, [Dependency]);
        }

        internal Source Source { get; } = new();

        internal Dependency Dependency { get; } = new();

        internal Stage Stage { get; }

        internal ResourceManager Manager { get; }

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            await _execution.DisposeAsync();
            await _memory.DisposeAsync();
        }
    }

    private sealed record Value(ulong Version);

    private sealed record Parent(ulong InputVersion);

    private sealed class Stage(Dependency dependency) : IResourceSourceStage<Value>
    {
        internal int Builds { get; private set; }

        internal int ReleaseAttempts { get; private set; }

        internal bool FailNextRelease { get; set; }

        public async ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            await context.OpenSourceAsync(_input, cancellationToken: cancellationToken);
            ulong version = dependency.Snapshot.Version;
            context.TrackRuntimeDependency(dependency.Id, version);
            return new(version);
        }

        public ValueTask ReleaseAsync(Value resource)
        {
            ReleaseAttempts++;
            if (FailNextRelease)
            {
                FailNextRelease = false;
                throw new IOException("Release must be retried.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ParentStage(ResourceDependencyLifetime lifetime) : IResourceSourceStage<Parent>
    {
        public async ValueTask<Parent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Value input = await context.RequireAsync(_key, lifetime, cancellationToken);
            return new(input.Version);
        }

        public ValueTask ReleaseAsync(Parent resource) => ValueTask.CompletedTask;
    }

    private sealed class Dependency : IResourceRuntimeDependency
    {
        private readonly object _gate = new();
        private ResourceRuntimeSnapshot _snapshot = new(1, true, 0);

        public event EventHandler? Changed;

        public string Id => "device";

        public ResourceRuntimeSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                {
                    return _snapshot;
                }
            }
        }

        internal void Set(ResourceRuntimeSnapshot snapshot)
        {
            lock (_gate)
            {
                _snapshot = snapshot;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Source : IResourceSource
    {
        public IResourceChangeSource Changes => Events;

        internal Events Events { get; } = new();

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("one");
    }

    private sealed class Events : IResourceChangeSource
    {
        private readonly Channel<ResourceSourceChange> _changes = Channel.CreateUnbounded<ResourceSourceChange>();

        internal TaskCompletionSource<IResourceWatchTargets> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ResourceSourceChange> WatchAsync(IResourceWatchTargets targets, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(targets);
            await foreach (ResourceSourceChange change in _changes.Reader.ReadAllAsync(cancellationToken))
            {
                yield return change;
            }
        }

        public IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default)
            => _changes.Reader.ReadAllAsync(cancellationToken);
    }

    private sealed class Snapshot(ResourceId id) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => "one";

        public override long? Length => 0;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(Stream.Null);

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
