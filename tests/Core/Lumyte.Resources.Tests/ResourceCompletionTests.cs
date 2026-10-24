using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Lumyte.Memory;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Checks internal completion delivery independently of ThreadPool timing.</summary>
public sealed class ResourceCompletionTests
{
    /// <summary>Dependency success and failure continue on their existing pipeline without a ThreadPool relay.</summary>
    /// <param name="fail">Whether the child fails before returning a value.</param>
    /// <returns>Completion of manager cleanup.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyCompletionStaysOnPipelineAsync(bool fail)
    {
        MemoryManager memory = new();
        var ready = new TaskCompletionSource();
        var context = new HostContext();
        object? managerGate = null;
        bool resumed = false;
        var stage = new Stage(async build =>
        {
            if (build.Address.Subresource == "child")
            {
                await ready.Task;
                if (fail)
                {
                    throw new IOException("child failed");
                }

                return new Value();
            }

            try
            {
                _ = await build.RequireAsync(new ResourceKey<Value>("asset", "child"));
                Assert.False(fail);
            }
            catch (IOException) when (fail)
            {
            }

            Assert.False(Monitor.IsEntered(managerGate!));
            resumed = true;
            return new Value();
        });
        var manager = new ResourceManager("completion", new Source(), [stage], memory, new InlineRuntime());
        managerGate = Gate(manager);
        Task<ResourceReference<Value>>? loading = null;
        context.Run(() => loading = manager.AcquireAsync(new ResourceKey<Value>("asset", "root")).AsTask());
        Assert.False(loading!.IsCompleted);
        CompleteWithoutContext(() => ready.SetResult());
        context.Until(() => loading.IsCompleted);
        Assert.True(resumed);
        Assert.Equal(0, context.ForeignPosts);
        if (fail)
        {
            // Handling RequireAsync does not suppress the tracked dependency failure.
            await Assert.ThrowsAnyAsync<Exception>(async () => await loading);
        }
        else
        {
            (await loading).Dispose();
        }

        await manager.DisposeAsync();
        await memory.DisposeAsync();
    }

    /// <summary>Source bookkeeping returns directly to the awaiting pipeline, outside the manager gate.</summary>
    /// <returns>Completion of manager cleanup.</returns>
    [Fact]
    public async Task SourceCompletionPostsDirectlyToPipelineAsync()
    {
        MemoryManager memory = new();
        var source = new Source();
        var context = new HostContext();
        object? managerGate = null;
        var stage = new Stage(async build =>
        {
            _ = await build.OpenSourceAsync(new ResourceId("asset"));
            Assert.False(Monitor.IsEntered(managerGate!));
            return new Value();
        });
        var manager = new ResourceManager("completion", source, [stage], memory, new InlineRuntime());
        managerGate = Gate(manager);
        Task<ResourceReference<Value>>? loading = null;
        context.Run(() => loading = manager.AcquireAsync(new ResourceKey<Value>("asset")).AsTask());
        Assert.False(loading!.IsCompleted);
        CompleteWithoutContext(() => source.Ready.SetResult(new Snapshot()));
        context.Until(() => loading.IsCompleted);
        Assert.Equal(0, context.ForeignPosts);
        (await loading).Dispose();
        await manager.DisposeAsync();
        await memory.DisposeAsync();
    }

    private static void CompleteWithoutContext(Action complete)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            complete();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    // Inspect only lock ownership; production code keeps the gate private.
    private static object Gate(ResourceManager manager)
        => typeof(ResourceManager).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;

    private sealed class HostContext : SynchronizationContext
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();
        private int _foreignPosts;

        internal int ForeignPosts => Volatile.Read(ref _foreignPosts);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (Environment.CurrentManagedThreadId != _thread)
            {
                Interlocked.Increment(ref _foreignPosts);
            }

            _pending.Enqueue((callback, state));
        }

        internal void Run(Action action)
        {
            SynchronizationContext? previous = Current;
            SetSynchronizationContext(this);
            try
            {
                action();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        internal void Until(Func<bool> done)
        {
            long started = Stopwatch.GetTimestamp();
            while (!done())
            {
                if (_pending.TryDequeue(out (SendOrPostCallback Callback, object? State) work))
                {
                    Run(() => work.Callback(work.State));
                }
                else
                {
                    Thread.Sleep(1);
                }

                Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10), "Pipeline stalled.");
            }
        }
    }

    private sealed class InlineRuntime : IResourceRuntime
    {
        public ValueTask<object> BuildAsync(IResourceStage stage, ResourceBuildContext context, CancellationToken cancellationToken)
            => context.ExecutePipelineAsync(stage, cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, object resource) => stage.ReleaseAsync(resource);
    }

    private sealed class Value;

    private sealed class Stage(Func<ResourceBuildContext, ValueTask<Value>> build) : IResourceSourceStage<Value>
    {
        public ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => build(context);

        public ValueTask ReleaseAsync(Value resource) => ValueTask.CompletedTask;
    }

    private sealed class Source : IResourceSource
    {
        internal TaskCompletionSource<ResourceSourceSnapshot> Ready { get; } = new();

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => new(Ready.Task);

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("1");
    }

    private sealed class Snapshot : ResourceSourceSnapshot
    {
        public override ResourceId Id => new("asset");

        public override string Revision => "1";

        public override long? Length => 0;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream());

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
