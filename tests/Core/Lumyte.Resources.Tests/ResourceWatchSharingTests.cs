using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Checks source-watch ownership across independent publications and unfinished builds.</summary>
public sealed class ResourceWatchSharingTests
{
    private static readonly ResourceId _input = new("input");
    private static readonly ResourceId _replacement = new("replacement");

    /// <summary>A source shared by multiple outputs remains watched until its last published user leaves.</summary>
    /// <returns>Completion of acquisition and independent reclamation.</returns>
    [Fact]
    public async Task SharedSourceRemainsWatchedUntilLastPublicationIsTrimmedAsync()
    {
        Source source = new();
        Stage stage = new();
        await using Lumyte.Memory.MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = CreateManager(source, stage, memory, execution);
        IResourceWatchTargets targets = await source.Events.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceReference<Value> first = await manager.RetainAsync(new ResourceKey<Value>("first"));
        using ResourceReference<Value> second = await manager.RetainAsync(new ResourceKey<Value>("second"));
        IReadOnlySet<ResourceId> captured = targets.Resources;
        Assert.Equal([_input], captured);
        first.Dispose();
        await manager.TrimAsync();
        Assert.Equal([_input], targets.Resources);
        second.Dispose();
        await manager.TrimAsync();
        Assert.Empty(targets.Resources);
        Assert.Equal([_input], captured);
    }

    /// <summary>A failed concurrent build cannot remove a watch needed by a pending build.</summary>
    /// <returns>Completion of failed and successful candidates.</returns>
    [Fact]
    public async Task FailedBuildPreservesPendingBuildSourceAsync()
    {
        Source source = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Stage stage = new()
        {
            AfterSource = async address =>
            {
                if (address.Id.Value == "failed")
                {
                    throw new IOException("Candidate failed.");
                }

                entered.TrySetResult();
                await resume.Task;
            },
        };
        await using Lumyte.Memory.MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = CreateManager(source, stage, memory, execution);
        IResourceWatchTargets targets = await source.Events.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<ResourceReference<Value>> pending = manager.RetainAsync(new ResourceKey<Value>("pending")).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.ThrowsAsync<IOException>(() => manager.RetainAsync(new ResourceKey<Value>("failed")).AsTask());
            Assert.Equal([_input], targets.Resources);
        }
        finally
        {
            resume.TrySetResult();
        }

        using ResourceReference<Value> reference = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([_input], targets.Resources);
        reference.Dispose();
        await manager.TrimAsync();
        Assert.Empty(targets.Resources);
    }

    /// <summary>Reload changes watched provenance while an old output lease and old target snapshot remain valid.</summary>
    /// <returns>Completion of source replacement and old-epoch reclamation.</returns>
    [Fact]
    public async Task ReloadReplacesWatchWithoutMutatingCapturedTargetsAsync()
    {
        Source source = new();
        Stage stage = new();
        await using Lumyte.Memory.MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        await using ResourceManager manager = CreateManager(source, stage, memory, execution);
        IResourceWatchTargets targets = await source.Events.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceReference<Value> reference = await manager.RetainAsync(new ResourceKey<Value>("value"));
        using ResourceLease<Value> previous = reference.Pin();
        IReadOnlySet<ResourceId> captured = targets.Resources;
        TaskCompletionSource published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        stage.Input = _replacement;
        source.Revisions[_input] = "two";
        source.Events.Notify(_input);
        await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([_replacement], targets.Resources);
        Assert.Equal([_input], captured);
        Assert.Equal(_input, previous.Value.Input);
        using (ResourceLease<Value> current = reference.Pin())
        {
            Assert.Equal(_replacement, current.Value.Input);
        }

        previous.Dispose();
        reference.Dispose();
        await manager.TrimAsync();
        Assert.Empty(targets.Resources);
    }

    private static ResourceManager CreateManager(Source source, Stage stage, Lumyte.Memory.MemoryManager memory, ResourceExecutionHost execution)
        => new("watch-sharing", source, [stage], memory, execution.Resources, reloadOptions: new() { DebounceDelay = TimeSpan.Zero, MaximumRetryCount = 0 });

    private sealed record Value(ResourceId Input);

    private sealed class Stage : IResourceSourceStage<Value>
    {
        internal ResourceId Input { get; set; } = _input;

        internal Func<ResourceAddress, Task>? AfterSource { get; init; }

        public async ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(Input, cancellationToken: cancellationToken);
            if (AfterSource is { } after)
            {
                await after(context.Address);
            }

            return new(snapshot.Id);
        }

        public ValueTask ReleaseAsync(Value resource) => ValueTask.CompletedTask;
    }

    private sealed class Source : IResourceSource
    {
        public IResourceChangeSource Changes => Events;

        internal Events Events { get; } = new();

        internal ConcurrentDictionary<ResourceId, string> Revisions { get; } = new();

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, Revisions.GetOrAdd(id, "one")));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(Revisions.GetOrAdd(id, "one"));
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

        internal void Notify(ResourceId id) => _changes.Writer.TryWrite(new(id, null));
    }

    private sealed class Snapshot(ResourceId id, string revision) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => revision;

        public override long? Length => 0;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(Stream.Null);

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
