using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Exercises scoped watcher ownership, shutdown ordering and notification filtering through public contracts.</summary>
public sealed class ResourceWatchRegistrationTests
{
    private static readonly ResourceId _known = new("asset");
    private static readonly ResourceKey<Value> _key = new(_known);

    /// <summary>Each named manager and DI scope owns one change source, which is stopped and disposed exactly once.</summary>
    /// <returns>Completion of all independently owned monitors.</returns>
    [Fact]
    public async Task NamedScopesOwnDistinctChangeSourcesAndDisposeThemOnceAsync()
    {
        WatchState state = new();
        ServiceCollection services = CreateServices(state, "first", "second");
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope firstScope = provider.CreateAsyncScope();
        await using AsyncServiceScope secondScope = provider.CreateAsyncScope();
        ResourceReloadService first = firstScope.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("first");
        ResourceReloadService second = firstScope.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("second");
        ResourceReloadService otherScope = secondScope.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("first");
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("first"));
        Assert.NotSame(first, second);
        Assert.NotSame(first, otherScope);
        Assert.Equal(3, state.ChangeSources.Count);

        first.Start();
        second.Start();
        otherScope.Start();
        await Task.WhenAll(state.ChangeSources.Select(source => source.Started.Task)).WaitAsync(TimeSpan.FromSeconds(10));
        await secondScope.DisposeAsync();
        await firstScope.DisposeAsync();

        Assert.All(state.ChangeSources, source =>
        {
            Assert.Equal(1, source.WatchCount);
            Assert.Equal(1, source.StopCount);
            Assert.Equal(1, source.DisposeCount);
            Assert.False(source.DisposedBeforeStop);
        });
        Assert.True(first.Completion.IsCompleted);
        Assert.True(second.Completion.IsCompleted);
        Assert.True(otherScope.Completion.IsCompleted);
        Assert.Equal(3, state.SourceDisposals);
    }

    /// <summary>Scope disposal cancels and joins a running rebuild before the manager releases its published output.</summary>
    /// <returns>Completion of cancellation, generation cleanup and DI service disposal.</returns>
    [Fact]
    public async Task ScopeDisposalStopsReloadBeforeManagerShutdownAsync()
    {
        WatchState state = new();
        ServiceCollection services = CreateServices(state, "main");
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceReloadService monitor = scope.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("main");
        ResourceManager manager = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("main");
        using (ResourceLease<Value> initial = await manager.AcquireAsync(_key))
        {
            Assert.Equal("one", initial.Value.Revision);
        }

        BlockingChangeSource changes = Assert.Single(state.ChangeSources);
        monitor.Start();
        await changes.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        state.BlockReload = true;
        state.Revision = "two";
        changes.Notify(new(_known, null));
        await state.BuildEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await scope.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(monitor.Completion.IsCompleted);
        Assert.True(state.BuildCancelled.Task.IsCompletedSuccessfully);
        Assert.False(changes.DisposedBeforeStop);
        string[] events = state.Events.ToArray();
        Assert.True(Array.IndexOf(events, "build cancelled") < Array.IndexOf(events, "release one"));
        Assert.True(Array.IndexOf(events, "watch stopped") < Array.IndexOf(events, "release one"));
        Assert.Equal(1, changes.DisposeCount);
        Assert.Equal(1, state.SourceDisposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await manager.AcquireAsync(_key));
    }

    /// <summary>An observable watcher failure does not interrupt disposal of the manager and its borrowed services.</summary>
    /// <returns>Completion of failed monitoring and successful scope cleanup.</returns>
    [Fact]
    public async Task WatchFailureDoesNotPreventScopeCleanupAsync()
    {
        IOException failure = new("watch failed");
        WatchState state = new() { WatchFailure = failure };
        ServiceCollection services = CreateServices(state, "main");
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceReloadService monitor = scope.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("main");
        ResourceManager manager = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("main");
        using (ResourceLease<Value> initial = await manager.AcquireAsync(_key))
        {
            Assert.Equal("one", initial.Value.Revision);
        }

        monitor.Start();
        IOException observed = await Assert.ThrowsAsync<IOException>(async () => await monitor.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(failure, observed);
        Assert.Same(failure, monitor.LastError);
        await scope.DisposeAsync();

        Assert.Contains("release one", state.Events);
        Assert.Equal(1, state.SourceDisposals);
        Assert.Equal(1, Assert.Single(state.ChangeSources).DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await manager.AcquireAsync(_key));
    }

    /// <summary>Change-source registration cannot be duplicated or mutated after the configuration callback.</summary>
    [Fact]
    public void WatchRegistrationRejectsDuplicateAndLateConfiguration()
    {
        ServiceCollection services = new();
        ResourceRegistrationBuilder? captured = null;
        services.AddResources("main", builder =>
        {
            captured = builder;
            builder.UseSource<TrackedSource>()
                .AddSourcePipeline<Value, ValueStage>()
                .WatchChanges<BlockingChangeSource>();
            Assert.Throws<InvalidOperationException>(() => builder.WatchChanges<BlockingChangeSource>());
        });

        Assert.Throws<InvalidOperationException>(() => captured!.WatchChanges<BlockingChangeSource>());
    }

    /// <summary>Unloaded files do not cause source access or prevent a loaded resource from being revalidated.</summary>
    /// <returns>Completion of targeted and full revalidation.</returns>
    [Fact]
    public async Task ReloadIgnoresUntrackedSourceHintsBeforeReadingRevisionsAsync()
    {
        WatchState state = new();
        ServiceCollection services = CreateServices(state, "main");
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager manager = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("main");
        using ResourceReference<Value> reference = await manager.CreateReferenceAsync(_key);
        state.RevisionReads.Clear();

        ResourceReloadResult unknown = await manager.ReloadAsync([new("untracked")]);
        Assert.Equal(ResourceReloadStatus.Unchanged, unknown.Status);
        Assert.Empty(state.RevisionReads);

        ResourceReloadResult mixed = await manager.ReloadAsync([new("untracked"), _known]);
        Assert.Equal(ResourceReloadStatus.Unchanged, mixed.Status);
        Assert.Equal([_known], state.RevisionReads);
        state.RevisionReads.Clear();

        state.Revision = "two";
        ResourceReloadResult all = await manager.ReloadAllAsync();
        Assert.Equal(ResourceReloadStatus.Published, all.Status);
        Assert.All(state.RevisionReads, id => Assert.Equal(_known, id));
        using ResourceLease<Value> current = await manager.AcquireAsync(_key);
        Assert.Equal("two", current.Value.Revision);
    }

    private static ServiceCollection CreateServices(WatchState state, params string[] names)
    {
        ServiceCollection services = new();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        foreach (string name in names)
        {
            services.AddResources(name, builder => builder.UseSource<TrackedSource>()
                .AddSourcePipeline<Value, ValueStage>()
                .WatchChanges<BlockingChangeSource>());
        }

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

        public ValueTask DisposeAsync()
        {
            state.SourceDisposals++;
            return ValueTask.CompletedTask;
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
