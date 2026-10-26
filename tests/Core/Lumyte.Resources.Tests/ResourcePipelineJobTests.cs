using System.Diagnostics;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Resources.Execution;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies pipeline job reuse, runtime boundaries and shared dependency lifetimes.</summary>
public sealed class ResourcePipelineJobTests
{
    /// <summary>A deep same-runtime pipeline needs only one admission slot, including asynchronous I/O.</summary>
    /// <param name="asynchronous">Whether the leaf waits for an external completion.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameRuntimePipelineSharesOneJob(bool asynchronous)
    {
        using Host host = new(1);
        var ready = new TaskCompletionSource();
        List<SynchronizationContext?> jobs = [];
        var stage = new Stage(async (context, token) =>
        {
            jobs.Add(SynchronizationContext.Current);
            int depth = int.Parse(context.Address.Subresource, System.Globalization.CultureInfo.InvariantCulture);
            if (depth != 0)
            {
                _ = await context.RequireAsync(Key((depth - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken: token);
            }
            else if (asynchronous)
            {
                await ready.Task;
                jobs.Add(SynchronizationContext.Current);
            }

            return new Value(depth);
        });
        ResourceManager manager = host.Create([stage]);
        Task<ResourceReference<Value>> loading = manager.AcquireAsync(Key("96")).AsTask();
        Assert.Empty(jobs);
        host.Until(() => jobs.Count >= 97);
        Assert.Equal(1, host.Default.Builds);
        if (asynchronous)
        {
            Assert.False(loading.IsCompleted);
            ready.SetResult();
        }

        using ResourceReference<Value> reference = host.Complete(loading);
        using ResourceLease<Value> lease = reference.Acquire();
        Assert.Equal(96, lease.Value.Number);
        Assert.NotNull(jobs[0]);
        Assert.All(jobs, job => Assert.Same(jobs[0], job));
        Assert.Equal(1, host.Default.Builds);
    }

    /// <summary>Each runtime transition starts a new job, even when environments share their executor.</summary>
    /// <param name="shareExecutor">Whether both environments use the exact same executor instance.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeTransitionsCreateSeparatePipelineJobs(bool shareExecutor)
    {
        using Host host = new(3);
        IResourceRuntime other = shareExecutor ? new Environment(host.Default) : new RecordingRuntime(host.Dispatcher);
        ResourceManager manager = host.Create(
            [new LeafStage(), new Transform<Leaf, Middle>(), new Transform<Middle, Root>()],
            [new(typeof(Node<Middle>), other)]);
        using ResourceReference<Node<Root>> reference = host.Complete(manager.AcquireAsync(new ResourceKey<Node<Root>>("asset")).AsTask());
        using ResourceLease<Node<Root>> lease = reference.Acquire();
        Node<Root> root = lease.Value;
        Node<Middle> middle = Assert.IsType<Node<Middle>>(root.Input);
        Node<Leaf> leaf = Assert.IsType<Node<Leaf>>(middle.Input);
        Assert.NotSame(root.Job, middle.Job);
        Assert.NotSame(middle.Job, leaf.Job);
        Assert.NotSame(root.Job, leaf.Job);
        Assert.Equal(shareExecutor ? 3 : 2, host.Default.Builds);
        if (!shareExecutor)
        {
            Assert.Equal(1, Assert.IsType<RecordingRuntime>(other).Builds);
        }
    }

    /// <summary>Requests explicitly made outside a workflow are dispatched instead of executing on an arbitrary thread.</summary>
    [Fact]
    public void OffWorkflowRequestDoesNotInlineDependency()
    {
        using Host host = new(2);
        SynchronizationContext? rootJob = null;
        SynchronizationContext? childJob = null;
        var stage = new Stage(async (context, token) =>
        {
            if (context.Address.Subresource == "child")
            {
                childJob = SynchronizationContext.Current;
                Assert.NotNull(childJob);
                return new Value(1);
            }

            rootJob = SynchronizationContext.Current;
            Value child = await Task.Run(async () => await context.RequireAsync(Key("child"), cancellationToken: token), token);
            return new Value(child.Number + 1);
        });
        ResourceManager manager = host.Create([stage]);
        using ResourceReference<Value> reference = host.Complete(manager.AcquireAsync(Key("root")).AsTask());
        Assert.NotSame(rootJob, childJob);
        Assert.Equal(2, host.Default.Builds);
    }

    /// <summary>A canceled parent keeps its shared child's job context alive until actual completion.</summary>
    [Fact]
    public void CanceledParentRetainsSharedDependencyExecution()
    {
        using Host host = new(2);
        using CancellationTokenSource canceled = new();
        var ready = new TaskCompletionSource();
        Dictionary<string, SynchronizationContext?> jobs = [];
        int sharedBuilds = 0;
        var stage = new Stage(async (context, token) =>
        {
            string name = context.Address.Subresource;
            jobs.Add(name, SynchronizationContext.Current);
            if (name == "shared")
            {
                sharedBuilds++;
                await ready.Task;
                Assert.Same(jobs[name], SynchronizationContext.Current);
                return new Value(1);
            }

            Value input = await context.RequireAsync(Key("shared"), cancellationToken: name == "first" ? canceled.Token : default);
            return new Value(input.Number + 1);
        });
        ResourceManager manager = host.Create([stage]);
        Task<ResourceReference<Value>> first = manager.AcquireAsync(Key("first")).AsTask();
        host.Until(() => jobs.ContainsKey("shared"));
        Task<ResourceReference<Value>> second = manager.AcquireAsync(Key("second")).AsTask();
        host.Until(() => jobs.ContainsKey("second"));
        Assert.Equal(2, host.Default.Builds);
        Assert.Same(jobs["first"], jobs["shared"]);
        Assert.NotSame(jobs["first"], jobs["second"]);
        canceled.Cancel();
        host.Pump();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        ready.SetResult();

        using ResourceReference<Value> reference = host.Complete(second);
        using ResourceLease<Value> lease = reference.Acquire();
        host.Until(() => first.IsCompleted);
        Assert.ThrowsAny<OperationCanceledException>(() => first.GetAwaiter().GetResult());
        Assert.Equal(2, lease.Value.Number);
        Assert.Equal(1, sharedBuilds);
    }

    /// <summary>Pending dependencies are drained and a successful output is reclaimed when an ignored dependency fails.</summary>
    [Fact]
    public void IgnoredDependencyFailureRetainsOutputUntilCleanup()
    {
        using Host host = new(1);
        var ready = new TaskCompletionSource();
        var output = new Value(42);
        int releases = 0;
        bool entered = false;
        SynchronizationContext? rootJob = null;
        Task<Value>? ignored = null;
        var stage = new Stage(async (context, token) =>
        {
            if (context.Address.Subresource == "child")
            {
                entered = true;
                await ready.Task;
                throw new IOException("Child build failed.");
            }

            rootJob = SynchronizationContext.Current;
            ignored = context.RequireAsync(Key("child")).AsTask();
            return output;
        });
        stage.Release = async value =>
        {
            Assert.Same(output, value);
            Assert.Same(rootJob, SynchronizationContext.Current);
            await Task.Yield();
            Assert.Same(rootJob, SynchronizationContext.Current);
            releases++;
        };
        ResourceManager manager = host.Create([stage]);
        Task<ResourceReference<Value>> loading = manager.AcquireAsync(Key("root")).AsTask();
        host.Until(() => entered);
        Assert.False(loading.IsCompleted);
        ready.SetResult();
        host.Until(() => loading.IsCompleted);
        Assert.ThrowsAny<Exception>(() => loading.GetAwaiter().GetResult());
        Assert.Equal(1, releases);
        Assert.NotNull(ignored);
        Assert.Throws<IOException>(() => ignored.GetAwaiter().GetResult());
        Assert.Equal(1, host.Default.Builds);
    }

    /// <summary>Fairness checkpoints must not strand the result of an ignored dependency after its build finishes.</summary>
    /// <param name="asynchronous">Whether the child suspends before consuming its CPU slice.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoredDependencyCompletesBeforeItsWorkflowCloses(bool asynchronous)
    {
        using Host host = new(1);
        var ready = new TaskCompletionSource();
        bool entered = false;
        Task<Value>? ignored = null;
        var stage = new Stage(async (context, _) =>
        {
            if (context.Address.Subresource == "child")
            {
                entered = true;
                if (asynchronous)
                {
                    await ready.Task;
                }

                Thread.Sleep(5);
                return new Value(1);
            }

            ignored = context.RequireAsync(Key("child")).AsTask();
            return new Value(2);
        });
        ResourceManager manager = host.Create([stage]);
        Task<ResourceReference<Value>> loading = manager.AcquireAsync(Key("root")).AsTask();
        host.Until(() => entered);
        ready.SetResult();
        using ResourceReference<Value> reference = host.Complete(loading);
        Assert.NotNull(ignored);
        Assert.True(ignored.IsCompleted, "The completed parent must not discard an accepted dependency continuation.");
        Assert.Equal(1, host.Complete(ignored).Number);
    }

    /// <summary>A cancellation continuation accepted before dependency completion is drained even when its caller returned.</summary>
    /// <param name="failChild">Whether the dependency fails after the cancellation notification.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoredCanceledDependencyCompletesBeforeItsWorkflowCloses(bool failChild)
    {
        using Host host = new(1);
        using CancellationTokenSource canceled = new();
        var ready = new TaskCompletionSource();
        bool entered = false;
        Task<Value>? ignored = null;
        var stage = new Stage(async (context, _) =>
        {
            if (context.Address.Subresource == "child")
            {
                entered = true;
                await ready.Task;
                Exception? notificationFailure = null;
                var notifier = new Thread(() =>
                {
                    try
                    {
                        canceled.Cancel();
                    }
                    catch (Exception error)
                    {
                        notificationFailure = error;
                    }
                });
                notifier.Start();

                // Post the cancellation continuation from another thread while this workflow dispatch is active.
                Assert.True(notifier.Join(TimeSpan.FromSeconds(10)), "Cancellation notification did not finish.");
                Assert.Null(notificationFailure);
                if (failChild)
                {
                    throw new IOException("Child failed after the cancellation callback was queued.");
                }

                return new Value(1);
            }

            ignored = context.RequireAsync(Key("child"), cancellationToken: canceled.Token).AsTask();
            return new Value(2);
        });
        ResourceManager manager = host.Create([stage]);
        Task<ResourceReference<Value>> loading = manager.AcquireAsync(Key("root")).AsTask();
        host.Until(() => entered);
        ready.SetResult();
        host.Until(() => loading.IsCompleted);
        if (failChild)
        {
            Assert.Throws<IOException>(() => loading.GetAwaiter().GetResult());
        }
        else
        {
            using ResourceReference<Value> reference = host.Complete(loading);
            using ResourceLease<Value> lease = reference.Acquire();
            Assert.Equal(2, lease.Value.Number);
        }

        Assert.NotNull(ignored);
        Assert.True(ignored.IsCompleted, "An accepted cancellation continuation must complete before its workflow closes.");
        Assert.ThrowsAny<OperationCanceledException>(() => ignored.GetAwaiter().GetResult());
    }

    /// <summary>An ignored pending dependency cannot become a second owner of the same output.</summary>
    [Fact]
    public void IgnoredDependencyAliasIsRejectedAfterItCompletes()
    {
        using Host host = new(1);
        var ready = new TaskCompletionSource();
        var output = new Value(42);
        bool entered = false;
        int releases = 0;
        Task<Value>? ignored = null;
        var stage = new Stage(async (context, _) =>
        {
            if (context.Address.Subresource == "child")
            {
                entered = true;
                await ready.Task;
                return output;
            }

            ignored = context.RequireAsync(Key("child")).AsTask();
            return output;
        });
        stage.Release = value =>
        {
            Assert.Same(output, value);
            releases++;
            return ValueTask.CompletedTask;
        };
        ResourceManager manager = host.Create([stage]);
        Task<ResourceReference<Value>> parent = manager.AcquireAsync(Key("root")).AsTask();
        host.Until(() => entered);
        Task<ResourceReference<Value>> child = manager.AcquireAsync(Key("child")).AsTask();
        ready.SetResult();
        using ResourceReference<Value> childReference = host.Complete(child);
        host.Until(() => parent.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => parent.GetAwaiter().GetResult());
        Assert.NotNull(ignored);
        Assert.Same(output, host.Complete(ignored));
        Assert.Equal(0, releases);
        childReference.Dispose();
        Assert.Equal(1, host.Complete(manager.TrimAsync().AsTask()));
        Assert.Equal(1, releases);
    }

    /// <summary>A long synchronous stage yields at the next dependency boundary without creating another job.</summary>
    [Fact]
    public void PipelineYieldsBetweenStagesWithinTheSameJob()
    {
        using Host host = new(1);
        bool rootBuilt = false;
        SynchronizationContext? leafJob = null;
        var stage = new Stage(async (context, token) =>
        {
            if (context.Address.Subresource == "leaf")
            {
                leafJob = SynchronizationContext.Current;
                Thread.Sleep(3);
                return new Value(1);
            }

            Value leaf = await context.RequireAsync(Key("leaf"));
            Assert.Same(leafJob, SynchronizationContext.Current);
            rootBuilt = true;
            return new Value(leaf.Number + 1);
        });
        ResourceManager manager = host.Create([stage]);
        Task<ResourceReference<Value>> loading = manager.AcquireAsync(Key("root")).AsTask();
        host.Pump();
        Assert.False(rootBuilt);
        using ResourceReference<Value> reference = host.Complete(loading);
        Assert.True(rootBuilt);
        Assert.Equal(1, host.Default.Builds);
    }

    private static ResourceKey<Value> Key(string name) => new("asset", name);

    private sealed class Host : IDisposable
    {
        private readonly MemoryManager _memory = new();
        private readonly ExecutionRuntime _execution;
        private readonly List<ResourceManager> _managers = [];

        internal Host(int capacity)
        {
            Default = new(Dispatcher);
            _execution = new([Dispatcher]);
            _execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions { MaxInFlightRuns = capacity, MaxCommands = capacity });
            _execution.StartAsync().GetAwaiter().GetResult();
        }

        internal ResourceRuntime Dispatcher { get; } = new();

        internal RecordingRuntime Default { get; }

        public void Dispose()
        {
            foreach (ResourceManager manager in _managers)
            {
                Complete(manager.DisposeAsync().AsTask());
            }

            Complete(_execution.StopAsync().AsTask());
            Complete(_memory.DisposeAsync().AsTask());
        }

        internal ResourceManager Create(IResourceStage[] stages, ResourceStageRuntime[]? bindings = null)
        {
            ResourceManager manager = new("pipeline", new UnusedSource(), stages, _memory, Default, stageRuntimes: bindings);
            _managers.Add(manager);
            return manager;
        }

        internal void Pump() => _execution.PumpJobs(1, TimeSpan.FromMilliseconds(2));

        internal void Until(Func<bool> done)
        {
            long start = Stopwatch.GetTimestamp();
            while (!done())
            {
                Pump();
                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException("Pipeline did not complete while pumping.");
                }

                Thread.Sleep(1);
            }
        }

        internal void Complete(Task task)
        {
            Until(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        internal T Complete<T>(Task<T> task)
        {
            Complete((Task)task);
            return task.GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingRuntime(IResourceRuntime inner) : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public bool ShouldYield => inner.Executor.ShouldYield;

        internal int Builds { get; private set; }

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            return inner.Executor.BuildAsync(context, cancellationToken);
        }

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => inner.Executor.ReleaseAsync(stage, runtime, resource);
    }

    private sealed class Environment(IResourceExecutor executor) : IResourceRuntime
    {
        public IResourceExecutor Executor => executor;
    }

    private sealed record Value(int Number);

    private sealed class Stage(Func<ResourceBuildContext, CancellationToken, ValueTask<Value>> build) : IResourceSourceStage<Value>
    {
        internal Func<Value, ValueTask> Release { get; set; } = _ => ValueTask.CompletedTask;

        public ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => build(context, cancellationToken);

        public ValueTask ReleaseAsync(Value value) => Release(value);
    }

    private sealed class Leaf;

    private sealed class Middle;

    private sealed class Root;

    private sealed record Node<T>(object? Input, SynchronizationContext? Job);

    private sealed class LeafStage : IResourceSourceStage<Node<Leaf>>
    {
        public ValueTask<Node<Leaf>> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new Node<Leaf>(null, SynchronizationContext.Current));

        public ValueTask ReleaseAsync(Node<Leaf> resource) => ValueTask.CompletedTask;
    }

    private sealed class Transform<TInput, TOutput> : IResourceTransform<Node<TInput>, Node<TOutput>>
    {
        public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

        public ResourceAddress GetInput(ResourceAddress output) => output;

        public ValueTask<Node<TOutput>> BuildAsync(Node<TInput> input, ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new Node<TOutput>(input, SynchronizationContext.Current));

        public ValueTask ReleaseAsync(Node<TOutput> resource) => ValueTask.CompletedTask;
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }
}
