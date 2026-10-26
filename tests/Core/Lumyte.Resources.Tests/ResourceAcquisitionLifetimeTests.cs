using System.Collections.Concurrent;
using Lumyte.Memory;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies logical ownership between initial publication and acquisition completion.</summary>
public sealed class ResourceAcquisitionLifetimeTests
{
    private static readonly ResourceKey<Value> _key = new("asset");

    /// <summary>Reload and reclamation cannot remove the current value while its first acquisition is completing.</summary>
    /// <returns>Completion of initial publication, replacement, reclamation and final release.</returns>
    [Fact]
    public async Task PendingAcquisitionRetainsReplacementAcrossReloadAndTrimAsync()
    {
        await using MemoryManager memory = new();
        using BlockingProvider provider = new();
        Source source = new();
        Stage stage = new(provider);
        await using ResourceManager manager = new("pending-acquisition", source, [stage], memory, new Runtime(provider));
        Task<ResourceReference<Value>> acquisition = manager.AcquireAsync(_key).AsTask();
        await stage.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        stage.Continue.TrySetResult();
        try
        {
            await provider.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(acquisition.IsCompleted);
            source.Revision = "two";
            ResourceReloadResult reload = await manager.ReloadForTestAsync([_key.Address.Id]);
            Assert.Equal(ResourceReloadStatus.Published, reload.Status);
            await manager.TrimAsync();
        }
        finally
        {
            provider.Resume();
        }

        using ResourceReference<Value> reference = await acquisition.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(reference.IsAvailable);
        using (ResourceLease<Value> lease = reference.Acquire())
        {
            Assert.Equal("two", lease.Value.Revision);
            Assert.False(lease.Value.Released);
        }

        reference.Dispose();
        await manager.TrimAsync();
        Assert.Equal(2, stage.Builds);
        Assert.All(stage.Outputs, value => Assert.True(value.Released));
        await manager.DisposeAsync();
    }

    /// <summary>Canceling one shared waiter releases only its logical ownership and preserves the remaining acquisition.</summary>
    /// <returns>Completion of cancellation, the shared build and resource reclamation.</returns>
    [Fact]
    public async Task CanceledWaiterDoesNotLeakOwnershipOrCancelOtherWaitersAsync()
    {
        await using MemoryManager memory = new();
        Stage stage = new();
        await using ResourceManager manager = new("shared-acquisition", new Source(), [stage], memory, new Runtime());
        using CancellationTokenSource cancellation = new();
        Task<ResourceReference<Value>> canceled = manager.AcquireAsync(_key, cancellation.Token).AsTask();
        await stage.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<ResourceReference<Value>> retained = manager.AcquireAsync(_key).AsTask();
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);
            Assert.False(retained.IsCompleted);
        }
        finally
        {
            stage.Continue.TrySetResult();
        }

        using ResourceReference<Value> reference = await retained.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(reference.IsAvailable);
        Assert.Equal(1, stage.Builds);
        Assert.Equal(0, await manager.TrimAsync());
        reference.Dispose();
        Assert.Equal(1, await manager.TrimAsync());
        Assert.True(Assert.Single(stage.Outputs).Released);
        await manager.DisposeAsync();
    }

    /// <summary>A failed shared build returns every waiting owner's reservation and permits a clean retry.</summary>
    /// <returns>Completion of shared failure, successful retry and manager shutdown.</returns>
    [Fact]
    public async Task FailedBuildReturnsAllPendingOwnershipBeforeRetryAsync()
    {
        await using MemoryManager memory = new();
        Stage stage = new(failFirst: true);
        await using ResourceManager manager = new("failed-acquisition", new Source(), [stage], memory, new Runtime());
        Task<ResourceReference<Value>> first = manager.AcquireAsync(_key).AsTask();
        await stage.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<ResourceReference<Value>> second = manager.AcquireAsync(_key).AsTask();
        stage.Continue.TrySetResult();
        await Assert.ThrowsAsync<IOException>(async () => await first.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<IOException>(async () => await second.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, stage.Builds);
        Assert.Empty(stage.Outputs);
        await manager.TrimAsync();
        using ResourceReference<Value> reference = await manager.AcquireAsync(_key);
        Assert.True(reference.IsAvailable);
        Assert.Equal(2, stage.Builds);
        reference.Dispose();
        Assert.Equal(1, await manager.TrimAsync());
        Assert.True(Assert.Single(stage.Outputs).Released);
        await manager.DisposeAsync();
    }

    private sealed class BlockingProvider : IResourceRuntimeDependency, IDisposable
    {
        private readonly ManualResetEventSlim _resume = new();
        private int _blockNextSnapshot;

        public event EventHandler? Changed
        {
            add
            {
            }

            remove
            {
            }
        }

        public string Id => "publication-barrier";

        public object Identity => this;

        public ResourceRuntimeSnapshot Snapshot
        {
            get
            {
                if (Interlocked.Exchange(ref _blockNextSnapshot, 0) != 0)
                {
                    Blocked.TrySetResult();
                    if (!_resume.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The pending acquisition was not resumed.");
                    }
                }

                return new(1, true, 0);
            }
        }

        internal TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => _resume.Dispose();

        internal void BlockNextSnapshot() => Volatile.Write(ref _blockNextSnapshot, 1);

        internal void Resume() => _resume.Set();
    }

    private sealed class Runtime(BlockingProvider? provider = null) : IResourceRuntime
    {
        public IResourceExecutor Executor { get; } = new Executor();

        public IReadOnlyList<IResourceRuntimeDependency> Dependencies => provider is null ? [] : [provider];
    }

    private sealed class Executor : IResourceExecutor
    {
        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => new(Task.Run(async () => await context.ExecuteAsync(cancellationToken), CancellationToken.None));

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
            => stage.ReleaseAsync(resource, runtime);
    }

    private sealed class Source : IResourceSource
    {
        internal string Revision { get; set; } = "one";

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, Revision));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(Revision);
    }

    private sealed class Snapshot(ResourceId id, string revision) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

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

        internal bool Released { get; set; }
    }

    private sealed class Stage(BlockingProvider? provider = null, bool failFirst = false) : IResourceSourceStage<Value>
    {
        private int _builds;

        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ConcurrentQueue<Value> Outputs { get; } = new();

        internal int Builds => Volatile.Read(ref _builds);

        public async ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            int build = Interlocked.Increment(ref _builds);
            if (build == 1)
            {
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
                if (failFirst)
                {
                    throw new IOException("The first shared build failed.");
                }
            }

            ResourceSourceSnapshot input = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            if (build == 1)
            {
                // This stage does not consume the provider. Its next observation is the acquire continuation.
                provider?.BlockNextSnapshot();
            }

            Value output = new(input.Revision);
            Outputs.Enqueue(output);
            return output;
        }

        public ValueTask ReleaseAsync(Value resource)
        {
            resource.Released = true;
            return ValueTask.CompletedTask;
        }
    }
}
