using System.Diagnostics;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies default job dispatch, asynchronous stage progress and resource runtime ownership.</summary>
public sealed class ResourceRuntimeTests
{
    /// <summary>A synchronous stage and its cleanup are queued without requiring execution APIs in the stage.</summary>
    [Fact]
    public void SynchronousStageAndReleaseWaitForTheJobPump()
    {
        bool built = false;
        bool released = false;
        var stage = new Stage((_, _) =>
        {
            built = true;
            return ValueTask.FromResult(new Value("synchronous"));
        });
        stage.Release = _ =>
        {
            released = true;
            return ValueTask.CompletedTask;
        };
        using CooperativeHost host = new(stage);
        Task<ResourceReference<Value>> loading = host.Manager.AcquireAsync(Key("value")).AsTask();
        Assert.False(built);
        host.Complete(loading).Dispose();
        Assert.True(built);

        Task<int> trim = host.Manager.TrimAsync().AsTask();
        Assert.False(released);
        Assert.Equal(1, host.Complete(trim));
        Assert.True(released);
    }

    /// <summary>Ordinary stages start and resume only while the cooperative job scheduler is pumped.</summary>
    [Fact]
    public void StageStartsAndResumesThroughJobs()
    {
        var ready = new TaskCompletionSource();
        int startedOn = 0;
        int resumedOn = 0;
        var stage = new Stage(async (_, _) =>
        {
            startedOn = Environment.CurrentManagedThreadId;
            await ready.Task;
            resumedOn = Environment.CurrentManagedThreadId;
            return new Value("complete");
        });
        using CooperativeHost host = new(stage);
        try
        {
            Task<ResourceReference<Value>> loading = host.Manager.AcquireAsync(Key("value")).AsTask();
            Assert.Equal(0, startedOn);
            Assert.False(loading.IsCompleted);

            host.Until(() => startedOn != 0);
            Assert.Equal(Environment.CurrentManagedThreadId, startedOn);
            Assert.Equal(0, resumedOn);
            ready.SetResult();
            Assert.Equal(0, resumedOn);
            Assert.False(loading.IsCompleted);

            using ResourceReference<Value> reference = host.Complete(loading);
            using ResourceLease<Value> lease = reference.Acquire();
            Assert.Equal(startedOn, resumedOn);
            Assert.Equal("complete", lease.Value.Label);
        }
        finally
        {
            ready.TrySetResult();
        }
    }

    /// <summary>A parent awaiting a child and a child awaiting I/O leave the single job slot available.</summary>
    [Fact]
    public void NestedDependencyAndIoDoNotOccupyTheOnlyJobSlot()
    {
        var ready = new TaskCompletionSource();
        bool childStarted = false;
        bool independentFinished = false;
        var stage = new Stage(async (context, token) =>
        {
            switch (context.Address.Subresource)
            {
                case "parent":
                    Value child = await context.RequireAsync(Key("child"), cancellationToken: token);
                    return new Value("parent:" + child.Label);
                case "child":
                    childStarted = true;
                    await ready.Task;
                    return new Value("child");
                default:
                    independentFinished = true;
                    return new Value("independent");
            }
        });
        using CooperativeHost host = new(stage);
        try
        {
            Task<ResourceReference<Value>> parent = host.Manager.AcquireAsync(Key("parent")).AsTask();
            host.Until(() => childStarted);
            Assert.False(parent.IsCompleted);

            using ResourceReference<Value> independent = host.Complete(host.Manager.AcquireAsync(Key("independent")).AsTask());
            Assert.True(independentFinished);
            Assert.False(parent.IsCompleted);
            ready.SetResult();
            using ResourceReference<Value> reference = host.Complete(parent);
            using ResourceLease<Value> lease = reference.Acquire();
            Assert.Equal("parent:child", lease.Value.Label);
        }
        finally
        {
            ready.TrySetResult();
        }
    }

    /// <summary>A canceled rebuild waits for the started stage and releases its actual unpublished output.</summary>
    [Fact]
    public void CanceledRebuildWaitsForAndReleasesTheActualStageOutput()
    {
        var ready = new TaskCompletionSource();
        var device = new DeviceDependency();
        bool replacementStarted = false;
        bool replacementReleased = false;
        var replacement = new Value("replacement");
        var stage = new Stage(async (context, _) =>
        {
            ulong version = device.Snapshot.Version;
            context.TrackRuntimeDependency(device.Id, version, providerIdentity: device);
            if (version == 1)
            {
                return new Value("original");
            }

            replacementStarted = true;
            await ready.Task;
            return replacement;
        });
        stage.Release = value =>
        {
            if (ReferenceEquals(value, replacement))
            {
                replacementReleased = true;
            }

            return ValueTask.CompletedTask;
        };
        using CooperativeHost host = new(stage, device);
        using ResourceReference<Value> original = host.Complete(host.Manager.AcquireAsync(Key("value")).AsTask());
        using CancellationTokenSource cancellation = new();
        device.Advance();
        Task<ResourceReloadResult> rebuilding = host.Manager.RebuildRuntimeAsync([device.Id], cancellation.Token).AsTask();
        try
        {
            host.Until(() => replacementStarted);
            cancellation.Cancel();
            Assert.False(rebuilding.IsCompleted);
            Assert.False(replacementReleased);

            ready.SetResult();
            Assert.ThrowsAny<OperationCanceledException>(() => host.Complete(rebuilding));
            Assert.True(replacementReleased);
            using ResourceLease<Value> lease = original.Acquire();
            Assert.Equal("original", lease.Value.Label);
        }
        finally
        {
            ready.TrySetResult();
        }
    }

    /// <summary>Full job capacity rejects cleanup without discarding the owned output or preventing a later retry.</summary>
    [Fact]
    public void QueueFullReleaseRetainsTheOutputUntilCapacityIsAvailable()
    {
        var ready = new TaskCompletionSource();
        var output = new Value("retained");
        bool blocked = false;
        int releases = 0;
        var stage = new Stage(async (context, _) =>
        {
            if (context.Address.Subresource == "blocker")
            {
                blocked = true;
                await ready.Task;
                return new Value("blocker");
            }

            return output;
        });
        stage.Release = value =>
        {
            if (ReferenceEquals(value, output))
            {
                releases++;
            }

            return ValueTask.CompletedTask;
        };
        using CooperativeHost host = new(stage, options: new ExecutionOptions { MaxCommands = 1 });
        host.Complete(host.Manager.AcquireAsync(Key("value")).AsTask()).Dispose();
        ResourceManager blockerManager = host.CreateSiblingManager(stage);
        Task<ResourceReference<Value>> loading = blockerManager.AcquireAsync(Key("blocker")).AsTask();
        try
        {
            host.Until(() => blocked);
            Exception error = Assert.ThrowsAny<Exception>(() => host.Complete(host.Manager.TrimAsync().AsTask()));
            Assert.Contains(nameof(ExecutionQueueFullException), error.ToString(), StringComparison.Ordinal);
            Assert.Equal(0, releases);

            ready.SetResult();
            using ResourceReference<Value> blocker = host.Complete(loading);
            Assert.Equal(1, host.Complete(host.Manager.TrimAsync().AsTask()));
            Assert.Equal(1, releases);
        }
        finally
        {
            ready.TrySetResult();
            host.Complete(loading).Dispose();
            host.Complete(blockerManager.DisposeAsync().AsTask());
        }
    }

    /// <summary>Asynchronous release failures remain visible and retry the same output through the scheduler.</summary>
    [Fact]
    public void FailedReleaseCanBeRetriedWithoutLosingTheOutput()
    {
        var ready = new TaskCompletionSource();
        int attempts = 0;
        var output = new Value("retained");
        var stage = new Stage((_, _) => ValueTask.FromResult(output));
        stage.Release = async value =>
        {
            Assert.Same(output, value);
            attempts++;
            if (attempts == 1)
            {
                await ready.Task;
                throw new InvalidOperationException("release failed after I/O");
            }
        };
        using CooperativeHost host = new(stage);
        host.Complete(host.Manager.AcquireAsync(Key("value")).AsTask()).Dispose();
        try
        {
            Task<int> trim = host.Manager.TrimAsync().AsTask();
            host.Until(() => attempts == 1);
            Assert.False(trim.IsCompleted);
            ready.SetResult();
            Exception error = Assert.ThrowsAny<Exception>(() => host.Complete(trim));
            Assert.Contains("release failed after I/O", error.ToString(), StringComparison.Ordinal);
            Assert.Equal(1, host.Complete(host.Manager.TrimAsync().AsTask()));
            Assert.Equal(2, attempts);
        }
        finally
        {
            ready.TrySetResult();
        }
    }

    /// <summary>Initial construction, runtime generation rebuild and release all use the injected dispatcher.</summary>
    [Fact]
    public void ManagerRoutesBuildRebuildAndReleaseThroughItsRuntime()
    {
        var device = new DeviceDependency();
        var stage = new Stage((context, _) =>
        {
            context.TrackRuntimeDependency(device.Id, device.Snapshot.Version, providerIdentity: device);
            return ValueTask.FromResult(new Value(device.Snapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        });
        using CooperativeHost host = new(stage, device);
        using ResourceReference<Value> reference = host.Complete(host.Manager.AcquireAsync(Key("value")).AsTask());
        using (ResourceLease<Value> first = reference.Acquire())
        {
            Assert.Equal("1", first.Value.Label);
            Assert.Equal(1, host.Dispatcher.Builds);
        }

        device.Advance();
        ResourceReloadResult rebuilt = host.Complete(host.Manager.RebuildRuntimeAsync([device.Id]).AsTask());
        Assert.Equal(ResourceReloadStatus.Published, rebuilt.Status);
        Assert.Equal(2, host.Dispatcher.Builds);
        using (ResourceLease<Value> second = reference.Acquire())
        {
            Assert.Equal("2", second.Value.Label);
        }

        reference.Dispose();
        host.Complete(host.Manager.TrimAsync().AsTask());
        Assert.Equal(2, host.Dispatcher.Releases);
    }

    /// <summary>Named managers share one default runtime while independent providers own separate endpoints.</summary>
    /// <returns>Completion of both providers and their resource managers.</returns>
    [Fact]
    public async Task DependencyInjectionSharesPerProviderAndIsolatesIndependentHostsAsync()
    {
        ServiceCollection services = new();
        services.AddMemoryManagement();
        foreach (string name in new[] { "first", "second" })
        {
            services.AddResources(name, builder => builder.UseSource<UnusedSource>().AddSourcePipeline<Value, ConstantStage>());
        }

        await using ServiceProvider first = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using ServiceProvider second = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.Same(first.GetRequiredService<ResourceRuntime>(), first.GetRequiredService<IResourceRuntime>());
        Assert.NotSame(first.GetRequiredService<IResourceRuntime>(), second.GetRequiredService<IResourceRuntime>());
        Assert.Same(first.GetRequiredService<ResourceRuntime>(), Assert.Single(first.GetServices<IExecutionModule>(), module => module.Name == "Resources"));
        Assert.Same(second.GetRequiredService<ResourceRuntime>(), Assert.Single(second.GetServices<IExecutionModule>(), module => module.Name == "Resources"));
        await using ResourceExecutionHost firstHost = new(first);
        await using ResourceExecutionHost secondHost = new(second);
        await using AsyncServiceScope firstScope = first.CreateAsyncScope();
        await using AsyncServiceScope secondScope = second.CreateAsyncScope();
        foreach (IServiceProvider provider in new[] { firstScope.ServiceProvider, secondScope.ServiceProvider })
        {
            foreach (string name in new[] { "first", "second" })
            {
                ResourceManager manager = provider.GetRequiredKeyedService<ResourceManager>(name);
                using ResourceReference<Value> reference = await manager.AcquireAsync(Key("value"));
                using ResourceLease<Value> lease = reference.Acquire();
                Assert.Equal("constant", lease.Value.Label);
            }
        }
    }

    private static ResourceKey<Value> Key(string selector) => new("asset", selector);

    private sealed class CooperativeHost : IDisposable
    {
        private readonly MemoryManager _memory = new();
        private readonly ExecutionRuntime _execution;

        internal CooperativeHost(IResourceStage stage, IResourceRuntimeDependency? dependency = null, ExecutionOptions? options = null)
        {
            var runtime = new ResourceRuntime();
            _execution = new ExecutionRuntime([runtime]);
            _execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, options ?? new ExecutionOptions());
            Complete(_execution.StartAsync().AsTask());
            Dispatcher = new RecordingRuntime(runtime);
            Manager = new ResourceManager("test", new UnusedSource(), [stage], _memory, Dispatcher, dependency is null ? [] : [dependency]);
        }

        internal ResourceManager Manager { get; }

        internal RecordingRuntime Dispatcher { get; }

        public void Dispose()
        {
            Until(() =>
            {
                try
                {
                    Complete(Manager.DisposeAsync().AsTask());
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            });
            Complete(_execution.StopAsync().AsTask());
            Complete(_memory.DisposeAsync().AsTask());
        }

        internal ResourceManager CreateSiblingManager(IResourceStage stage)
            => new("sibling", new UnusedSource(), [stage], _memory, Dispatcher);

        internal void Until(Func<bool> done)
        {
            long start = Stopwatch.GetTimestamp();
            while (!done())
            {
                _execution.PumpJobs(16, TimeSpan.FromMilliseconds(2));
                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException("Resource jobs did not complete while the host was pumping.");
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

        internal int Releases { get; private set; }

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            return inner.Executor.BuildAsync(context, cancellationToken);
        }

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
        {
            Releases++;
            return inner.Executor.ReleaseAsync(stage, runtime, resource);
        }
    }

    private sealed class Value(string label)
    {
        internal string Label { get; } = label;
    }

    private sealed class Stage(Func<ResourceBuildContext, CancellationToken, ValueTask<Value>> build) : IResourceSourceStage<Value>
    {
        internal Func<Value, ValueTask> Release { get; set; } = _ => ValueTask.CompletedTask;

        public ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => build(context, cancellationToken);

        public ValueTask ReleaseAsync(Value resource) => Release(resource);
    }

    private sealed class ConstantStage : IResourceSourceStage<Value>
    {
        public ValueTask<Value> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => ValueTask.FromResult(new Value("constant"));

        public ValueTask ReleaseAsync(Value resource) => ValueTask.CompletedTask;
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }

    private sealed class DeviceDependency : IResourceRuntimeDependency
    {
        public event EventHandler? Changed;

        public string Id => "device";

        public ResourceRuntimeSnapshot Snapshot { get; private set; } = new(1, true, 0);

        internal void Advance()
        {
            Snapshot = new(Snapshot.Version + 1, true, 0);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
