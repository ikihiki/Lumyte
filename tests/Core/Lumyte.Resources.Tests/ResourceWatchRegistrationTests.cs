using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Exercises shared watcher ownership, shutdown ordering and notification filtering through public contracts.</summary>
public sealed class ResourceWatchRegistrationTests
{
    private static readonly ResourceId _known = new("asset");
    private static readonly ResourceKey<Value> _key = new(_known);

    /// <summary>The manager owns subscription lifetime and tracks only inputs retained by the loaded graph.</summary>
    /// <returns>Completion of automatic startup, signal-driven reload and watcher shutdown.</returns>
    [Fact]
    public async Task DirectManagerOwnsWatchingAndUpdatesLoadedTargetsAsync()
    {
        WatchState state = new();
        await using Lumyte.Memory.MemoryManager memory = new();
        await using TrackedSource source = new(state);
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = new(
            "direct",
            source,
            [new ValueStage(state)],
            memory,
            execution.Resources,
            reloadOptions: new() { DebounceDelay = TimeSpan.Zero, MaximumRetryCount = 0 });
        var changes = (BlockingChangeSource)source.Changes!;
        ResourceManager monitor = manager;
        await changes.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        IReadOnlySet<ResourceId> initialTargets = changes.Targets!.Resources;
        Assert.Empty(initialTargets);
        TaskCompletionSource firstCheck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Reloaded += (_, args) =>
        {
            firstCheck.TrySetResult();
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        using ResourceReference<Value> reference = await manager.AcquireAsync(_key);
        await firstCheck.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([_known], changes.Targets.Resources);
        Assert.Empty(initialTargets);
        Assert.Throws<InvalidOperationException>(() => manager.DisposeAsync());
        Assert.Equal(0, changes.StopCount);
        state.Revision = "two";
        changes.Notify(new(_known, null));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (ResourceLease<Value> value = reference.Acquire())
        {
            Assert.Equal("two", value.Value.Revision);
        }

        reference.Dispose();
        await manager.TrimAsync();
        Assert.Empty(changes.Targets.Resources);
        await manager.DisposeAsync();
        Assert.Equal(1, changes.WatchCount);
        Assert.Equal(1, changes.StopCount);
        Assert.Equal(0, changes.DisposeCount);
        Assert.DoesNotContain(typeof(ResourceManager).GetMethods(), method => method.Name is "ReloadAsync" or "ReloadAllAsync");
    }

    /// <summary>Trimming a build-only input keeps its provenance watched while a published parent uses it.</summary>
    /// <returns>Completion of build-input reclamation and automatic parent reload.</returns>
    [Fact]
    public async Task TrimmedBuildInputsRemainWatchedUntilTheirParentIsReleasedAsync()
    {
        WatchState state = new();
        await using Lumyte.Memory.MemoryManager memory = new();
        await using TrackedSource source = new(state);
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = new(
            "build-input",
            source,
            [new ValueStage(state), new ParentStage()],
            memory,
            execution.Resources,
            reloadOptions: new() { DebounceDelay = TimeSpan.Zero, MaximumRetryCount = 0 });
        var changes = (BlockingChangeSource)source.Changes!;
        await changes.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceReference<Parent> parent = await manager.AcquireAsync(new ResourceKey<Parent>("parent"));
        await manager.TrimAsync();
        Assert.Contains("release one", state.Events);
        Assert.Equal([_known], changes.Targets!.Resources);
        TaskCompletionSource published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        state.Revision = "two";
        changes.Notify(new(_known, null));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (ResourceLease<Parent> current = parent.Acquire())
        {
            Assert.Equal("two", current.Value.Revision);
        }

        parent.Dispose();
        await manager.TrimAsync();
        Assert.Empty(changes.Targets.Resources);
    }

    /// <summary>A source without notifications loads normally without allocating a background reload worker.</summary>
    /// <returns>Completion of loading and source ownership checks.</returns>
    [Fact]
    public async Task SourcesWithoutNotificationsDoNotStartMonitoringAsync()
    {
        WatchState state = new() { EnableChanges = false };
        await using Lumyte.Memory.MemoryManager memory = new();
        await using TrackedSource source = new(state);
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = new("immutable", source, [new ValueStage(state)], memory, execution.Resources);
        Assert.False(manager.IsHotReloadEnabled);
        Assert.Empty(state.ChangeSources);
        using ResourceReference<Value> reference = await manager.AcquireAsync(_key);
        using ResourceLease<Value> value = reference.Acquire();
        Assert.Equal("one", value.Value.Revision);
    }

    /// <summary>Reload settings can disable source monitoring without changing the stream provider or disposing its notifications.</summary>
    /// <returns>Completion of loading without an active watcher.</returns>
    [Fact]
    public async Task DisabledReloadDoesNotSubscribeToTheSourceAsync()
    {
        WatchState state = new();
        await using Lumyte.Memory.MemoryManager memory = new();
        await using TrackedSource source = new(state);
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = new(
            "disabled",
            source,
            [new ValueStage(state)],
            memory,
            execution.Resources,
            reloadOptions: new() { Enabled = false });
        Assert.False(manager.IsHotReloadEnabled);
        using ResourceReference<Value> reference = await manager.AcquireAsync(_key);
        reference.Dispose();
        await manager.DisposeAsync();
        BlockingChangeSource changes = Assert.Single(state.ChangeSources);
        Assert.Equal(0, changes.WatchCount);
        Assert.Equal(0, changes.DisposeCount);
    }

    /// <summary>All scopes share one source subscription, and scope disposal leaves the manager running.</summary>
    /// <returns>Completion of shared monitoring and ordered application shutdown.</returns>
    [Fact]
    public async Task ScopesShareOneManagerAndSourceSubscriptionAsync()
    {
        WatchState state = new();
        ServiceCollection services = CreateServices(state);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using ResourceExecutionHost execution = new(provider);
        await using AsyncServiceScope firstScope = provider.CreateAsyncScope();
        await using AsyncServiceScope secondScope = provider.CreateAsyncScope();
        await using ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        Assert.Same(manager, firstScope.ServiceProvider.GetRequiredService<ResourceManager>());
        Assert.Same(manager, secondScope.ServiceProvider.GetRequiredService<ResourceManager>());
        ResourceManager monitor = manager;
        BlockingChangeSource changes = Assert.Single(state.ChangeSources);
        await changes.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await secondScope.DisposeAsync();
        await firstScope.DisposeAsync();
        Assert.Equal(1, changes.WatchCount);
        Assert.Equal(0, changes.StopCount);
        Assert.Equal(0, changes.DisposeCount);
        Assert.False(monitor.HotReloadCompletion.IsCompleted);

        using (ResourceReference<Value> reference = await manager.AcquireAsync(_key))
        using (ResourceLease<Value> lease = reference.Acquire())
        {
            Assert.Equal("one", lease.Value.Revision);
        }

        await manager.DisposeAsync();
        Assert.True(monitor.HotReloadCompletion.IsCompleted);
        Assert.Equal(1, changes.StopCount);
        Assert.Equal(0, state.SourceDisposals);
        await execution.DisposeAsync();
        await provider.DisposeAsync();
        Assert.Equal(1, changes.DisposeCount);
        Assert.Equal(1, state.SourceDisposals);
        Assert.False(changes.DisposedBeforeStop);
    }

    /// <summary>Manager disposal cancels and joins a running rebuild before releasing its published output.</summary>
    /// <returns>Completion of cancellation, generation cleanup and DI service disposal.</returns>
    [Fact]
    public async Task ManagerDisposalStopsReloadBeforeReleasingOutputsAsync()
    {
        WatchState state = new();
        ServiceCollection services = CreateServices(state);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using ResourceExecutionHost execution = new(provider);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager monitor = scope.ServiceProvider.GetRequiredService<ResourceManager>();
        await using ResourceManager manager = scope.ServiceProvider.GetRequiredService<ResourceManager>();
        using (ResourceLease<Value> initial = await manager.AcquireLeaseForTestAsync(_key))
        {
            Assert.Equal("one", initial.Value.Revision);
        }

        BlockingChangeSource changes = Assert.Single(state.ChangeSources);
        await changes.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        state.BlockReload = true;
        state.Revision = "two";
        changes.Notify(new(_known, null));
        await state.BuildEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await scope.DisposeAsync();
        Assert.Equal(0, state.SourceDisposals);
        await execution.DisposeAsync();
        await provider.DisposeAsync();

        Assert.True(monitor.HotReloadCompletion.IsCompleted);
        Assert.True(state.BuildCancelled.Task.IsCompletedSuccessfully);
        Assert.False(changes.DisposedBeforeStop);
        string[] events = state.Events.ToArray();
        Assert.True(Array.IndexOf(events, "build cancelled") < Array.IndexOf(events, "release one"));
        Assert.True(Array.IndexOf(events, "watch stopped") < Array.IndexOf(events, "release one"));
        Assert.Equal(1, changes.DisposeCount);
        Assert.Equal(1, state.SourceDisposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await manager.AcquireLeaseForTestAsync(_key));
    }

    /// <summary>An observable watcher failure does not interrupt disposal of the manager and its borrowed services.</summary>
    /// <returns>Completion of failed monitoring and successful application cleanup.</returns>
    [Fact]
    public async Task WatchFailureDoesNotPreventManagerCleanupAsync()
    {
        IOException failure = new("watch failed");
        WatchState state = new() { WatchFailure = failure };
        ServiceCollection services = CreateServices(state);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using ResourceExecutionHost execution = new(provider);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager monitor = scope.ServiceProvider.GetRequiredService<ResourceManager>();
        await using ResourceManager manager = scope.ServiceProvider.GetRequiredService<ResourceManager>();
        using (ResourceLease<Value> initial = await manager.AcquireLeaseForTestAsync(_key))
        {
            Assert.Equal("one", initial.Value.Revision);
        }

        IOException observed = await Assert.ThrowsAsync<IOException>(async () => await monitor.HotReloadCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(failure, observed);
        Assert.Same(failure, monitor.LastReloadError);
        await manager.DisposeAsync();
        await scope.DisposeAsync();
        await execution.DisposeAsync();
        await provider.DisposeAsync();

        Assert.Contains("release one", state.Events);
        Assert.Equal(1, state.SourceDisposals);
        Assert.Equal(1, Assert.Single(state.ChangeSources).DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await manager.AcquireLeaseForTestAsync(_key));
    }

    /// <summary>Reload settings cannot be duplicated or mutated after the configuration callback.</summary>
    [Fact]
    public void ReloadConfigurationRejectsDuplicateAndLateConfiguration()
    {
        ServiceCollection services = new();
        ResourceRegistrationBuilder? captured = null;
        services.AddResources(builder =>
        {
            captured = builder;
            builder.UseSource<TrackedSource>()
                .AddSourcePipeline<Value, ValueStage>()
                .ConfigureHotReload(new ResourceReloadOptions());
            Assert.Throws<InvalidOperationException>(() => builder.ConfigureHotReload(new ResourceReloadOptions()));
        });

        Assert.Throws<InvalidOperationException>(() => captured!.ConfigureHotReload(new ResourceReloadOptions()));
    }

    /// <summary>Unloaded files do not cause source access or prevent a loaded resource from being revalidated.</summary>
    /// <returns>Completion of targeted and full revalidation.</returns>
    [Fact]
    public async Task ReloadIgnoresUntrackedSourceHintsBeforeReadingRevisionsAsync()
    {
        WatchState state = new();
        ServiceCollection services = CreateServices(state);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using ResourceExecutionHost execution = new(provider);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await using ResourceManager manager = scope.ServiceProvider.GetRequiredService<ResourceManager>();
        TaskCompletionSource initial = new(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Reloaded += (_, _) => initial.TrySetResult();
        using ResourceReference<Value> reference = await manager.AcquireAsync(_key);
        await initial.Task.WaitAsync(TimeSpan.FromSeconds(10));
        state.RevisionReads.Clear();

        ResourceReloadResult unknown = await manager.ReloadForTestAsync([new("untracked")]);
        Assert.Equal(ResourceReloadStatus.Unchanged, unknown.Status);
        Assert.Empty(state.RevisionReads);

        ResourceReloadResult mixed = await manager.ReloadForTestAsync([new("untracked"), _known]);
        Assert.Equal(ResourceReloadStatus.Unchanged, mixed.Status);
        Assert.Equal([_known], state.RevisionReads);
        state.RevisionReads.Clear();

        state.Revision = "two";
        ResourceReloadResult all = await manager.ReloadAllForTestAsync();
        Assert.Equal(ResourceReloadStatus.Published, all.Status);
        Assert.All(state.RevisionReads, id => Assert.Equal(_known, id));
        using ResourceLease<Value> current = await manager.AcquireLeaseForTestAsync(_key);
        Assert.Equal("two", current.Value.Revision);
    }

    private static ServiceCollection CreateServices(WatchState state)
    {
        ServiceCollection services = new();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<TrackedSource>()
            .AddSourcePipeline<Value, ValueStage>()
            .ConfigureHotReload(new ResourceReloadOptions()));

        return services;
    }

    private sealed class WatchState
    {
        private string _revision = "one";

        internal ConcurrentQueue<BlockingChangeSource> ChangeSources { get; } = new();

        internal ConcurrentQueue<ResourceId> RevisionReads { get; } = new();

        internal ConcurrentQueue<string> Events { get; } = new();

        internal TaskCompletionSource BuildEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource BuildCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal string Revision
        {
            get => Volatile.Read(ref _revision);
            set => Volatile.Write(ref _revision, value);
        }

        internal bool EnableChanges { get; init; } = true;

        internal bool BlockReload { get; set; }

        internal Exception? WatchFailure { get; init; }

        internal int SourceDisposals { get; set; }
    }

    private sealed class BlockingChangeSource : IResourceChangeSource, IAsyncDisposable
    {
        private readonly WatchState _state;
        private readonly Channel<ResourceSourceChange> _changes = Channel.CreateUnbounded<ResourceSourceChange>();

        public BlockingChangeSource(WatchState state)
        {
            _state = state;
            state.ChangeSources.Enqueue(this);
        }

        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int WatchCount { get; private set; }

        internal int StopCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal bool DisposedBeforeStop { get; private set; }

        internal IResourceWatchTargets? Targets { get; private set; }

        public IAsyncEnumerable<ResourceSourceChange> WatchAsync(IResourceWatchTargets targets, CancellationToken cancellationToken = default)
        {
            Targets = targets;
            return WatchAsync(cancellationToken);
        }

        public async IAsyncEnumerable<ResourceSourceChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            WatchCount++;
            Started.TrySetResult();
            try
            {
                if (_state.WatchFailure is { } failure)
                {
                    throw failure;
                }

                await foreach (ResourceSourceChange change in _changes.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return change;
                }
            }
            finally
            {
                StopCount++;
                _state.Events.Enqueue("watch stopped");
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposedBeforeStop |= WatchCount != StopCount;
            return ValueTask.CompletedTask;
        }

        internal void Notify(ResourceSourceChange change) => Assert.True(_changes.Writer.TryWrite(change));
    }

    private sealed class TrackedSource(WatchState state) : IResourceSource, IAsyncDisposable
    {
        public IResourceChangeSource? Changes { get; } = state.EnableChanges ? new BlockingChangeSource(state) : null;

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            Assert.Equal(_known, id);
            return ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(state.Revision));
        }

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            state.RevisionReads.Enqueue(id);
            if (id != _known)
            {
                throw new IOException("Untracked files must not be read while revalidating loaded resources.");
            }

            return ValueTask.FromResult<string?>(state.Revision);
        }

        public async ValueTask DisposeAsync()
        {
            state.SourceDisposals++;
            if (Changes is IAsyncDisposable changes)
            {
                await changes.DisposeAsync();
            }
        }
    }

    private sealed class Snapshot(string revision) : ResourceSourceSnapshot
    {
        public override ResourceId Id => _known;

        public override string Revision => revision;

        public override long? Length => 0;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream());

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Parent(string revision)
    {
        internal string Revision { get; } = revision;
    }

    private sealed class ParentStage : IResourceSourceStage<Parent>
    {
        public async ValueTask<Parent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Value input = await context.RequireAsync(_key, ResourceDependencyLifetime.Build, cancellationToken);
            return new(input.Revision);
        }

        public ValueTask ReleaseAsync(Parent resource) => ValueTask.CompletedTask;
    }

    private sealed class Value(string revision)
    {
        internal string Revision { get; } = revision;
    }

    private sealed class ValueStage(WatchState state) : IResourceSourceStage<Value>
    {
        public async ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceSourceSnapshot source = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            if (source.Revision == "two" && state.BlockReload)
            {
                state.BuildEntered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    state.Events.Enqueue("build cancelled");
                    state.BuildCancelled.TrySetResult();
                    throw;
                }
            }

            return new(source.Revision);
        }

        public ValueTask ReleaseAsync(Value resource)
        {
            state.Events.Enqueue($"release {resource.Revision}");
            return ValueTask.CompletedTask;
        }
    }
}
