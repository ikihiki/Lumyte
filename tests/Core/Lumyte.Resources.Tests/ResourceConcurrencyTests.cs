using System.Collections.Concurrent;
using System.Globalization;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Exercises publication races and retained graph provenance through dependency injection.</summary>
public sealed class ResourceConcurrencyTests
{
    /// <summary>A new acquisition cannot join a completed load while its detached cleanup is still running.</summary>
    /// <returns>Completion of the overlapping load, trim and cleanup operations.</returns>
    [Fact]
    public async Task AcquireAfterTrimDoesNotJoinPublishedLoadWithPendingCleanupAsync()
    {
        await using Fixture fixture = new();
        TaskCompletionSource valueStarted = Signal();
        TaskCompletionSource finishValue = Signal();
        TaskCompletionSource cleanupStarted = Signal();
        TaskCompletionSource finishCleanup = Signal();
        int valueBuilds = 0;
        TestValue original = new("original");
        fixture.Plan.Builds["value"] = async (_, token) =>
        {
            if (Interlocked.Increment(ref valueBuilds) == 1)
            {
                valueStarted.SetResult();
                await finishValue.Task.WaitAsync(token);
                return original;
            }

            return new("replacement");
        };
        fixture.Plan.Builds["unpublished"] = (_, _) => ValueTask.FromResult(new TestValue("unpublished"));
        fixture.Plan.Builds["failed"] = async (context, token) =>
        {
            await context.RequireAsync(Key("unpublished"), cancellationToken: token);
            throw new InvalidOperationException("Deliberately failed sibling build.");
        };
        fixture.Plan.Release = async resource =>
        {
            if (resource.Label == "unpublished")
            {
                cleanupStarted.TrySetResult();
                await finishCleanup.Task;
            }
        };
        using CancellationTokenSource cancellation = new();
        Task<ResourceLease<TestValue>> abandoned = fixture.Manager.AcquireLeaseForTestAsync(Key("value"), cancellation.Token).AsTask();
        await valueStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.AcquireLeaseForTestAsync(Key("failed")));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await abandoned);
            finishValue.SetResult();
            await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, await fixture.Manager.TrimAsync());
            Assert.True(original.Released);
            using ResourceLease<TestValue> fresh = await fixture.Manager.AcquireLeaseForTestAsync(Key("value")).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("replacement", fresh.Value.Label);
            Assert.False(fresh.Value.Released);
            Assert.Equal(2, valueBuilds);
        }
        finally
        {
            finishValue.TrySetResult();
            finishCleanup.TrySetResult();
        }
    }

    /// <summary>Cycle checks still use a live parent's provenance after its build-only child is trimmed.</summary>
    /// <returns>Completion of trimming and the rejected reverse dependency.</returns>
    [Fact]
    public async Task CachedMetadataDetectsCycleThroughTrimmedBuildDependencyAsync()
    {
        await using Fixture fixture = new();
        bool reverse = false;
        fixture.Plan.Builds["parent"] = async (context, token) =>
        {
            await context.RequireAsync(Key("child"), ResourceDependencyLifetime.Build, token);
            return new("parent");
        };
        fixture.Plan.Builds["child"] = async (context, token) =>
        {
            if (reverse)
            {
                await context.RequireAsync(Key("parent"), ResourceDependencyLifetime.Build, token);
            }

            return new("child");
        };
        using ResourceLease<TestValue> parent = await fixture.Manager.AcquireLeaseForTestAsync(Key("parent"));
        Assert.Equal(1, await fixture.Manager.TrimAsync());
        reverse = true;

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Manager.AcquireLeaseForTestAsync(Key("child")).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parent", error.Message, StringComparison.Ordinal);
        Assert.Contains("child", error.Message, StringComparison.Ordinal);
        Assert.True(parent.IsUsable);
        Assert.Equal("parent", parent.Value.Label);
    }

    /// <summary>Advancing an available runtime without invalidation leaves retained older revisions usable.</summary>
    /// <returns>Completion of the runtime update and explicit rebuild.</returns>
    [Fact]
    public async Task RuntimeAdvanceWithoutInvalidationPreservesOlderLeasesAsync()
    {
        await using Fixture fixture = new();
        fixture.Plan.Builds["value"] = (context, _) =>
        {
            ulong version = fixture.Runtime.Snapshot.Version;
            context.TrackRuntimeDependency("runtime", version);
            return ValueTask.FromResult(new TestValue($"version:{version}"));
        };
        using ResourceLease<TestValue> old = await fixture.Manager.AcquireLeaseForTestAsync(Key("value"));
        fixture.Runtime.Set(new(2, true, 0));
        Assert.True(old.IsUsable);
        Assert.Equal("version:1", old.Value.Label);

        ResourceReloadResult result = await fixture.Manager.RebuildRuntimeAsync(["runtime"]);
        Assert.Equal(ResourceReloadStatus.Published, result.Status);
        using ResourceLease<TestValue> current = await fixture.Manager.AcquireLeaseForTestAsync(Key("value"));
        Assert.Equal("version:2", current.Value.Label);
        Assert.NotEqual(old.Revision, current.Revision);
        Assert.True(old.IsUsable);
        Assert.Equal("version:1", old.Value.Label);
    }

    /// <summary>Warm value and usability reads do not allocate regardless of runtime participation.</summary>
    /// <param name="registered">Whether a runtime provider is registered.</param>
    /// <param name="tracked">Whether the resource depends on the runtime.</param>
    /// <returns>Completion of resource construction and the allocation measurement.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LeaseReadsDoNotAllocateAsync(bool registered, bool tracked)
    {
        await using Fixture fixture = new(null, registered);
        fixture.Plan.Builds["value"] = (context, _) =>
        {
            if (tracked)
            {
                context.TrackRuntimeDependency("runtime", fixture.Runtime.Snapshot.Version);
            }

            return ValueTask.FromResult(new TestValue("value"));
        };
        using ResourceLease<TestValue> lease = await fixture.Manager.AcquireLeaseForTestAsync(Key("value"));
        for (int i = 0; i < 10000; i++)
        {
            GC.KeepAlive(lease.Value);
            _ = lease.IsUsable;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            GC.KeepAlive(lease.Value);
            _ = lease.IsUsable;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    /// <summary>Resident paths preserve older runtime versions, and build-only edges stop invalidation propagation.</summary>
    /// <returns>Completion of the shared dependency graph and runtime invalidation.</returns>
    [Fact]
    public async Task ResidentPathsPreserveDistinctVersionsAndBuildBoundariesAsync()
    {
        await using Fixture fixture = new();
        fixture.Plan.Builds["old"] = (context, _) =>
        {
            context.TrackRuntimeDependency("runtime", fixture.Runtime.Snapshot.Version);
            return ValueTask.FromResult(new TestValue("old"));
        };
        using ResourceLease<TestValue> old = await fixture.Manager.AcquireLeaseForTestAsync(Key("old"));
        fixture.Runtime.Set(new(2, true, 0));
        foreach (string branch in new[] { "left", "right" })
        {
            fixture.Plan.Builds[branch] = async (context, token) =>
            {
                await context.RequireAsync(Key("old"), cancellationToken: token);
                return new(branch);
            };
        }

        fixture.Plan.Builds["parent"] = async (context, token) =>
        {
            context.TrackRuntimeDependency("runtime", fixture.Runtime.Snapshot.Version);
            await context.RequireAsync(Key("left"), cancellationToken: token);
            await context.RequireAsync(Key("right"), cancellationToken: token);
            return new("parent");
        };
        fixture.Plan.Builds["compiled"] = async (context, token) =>
        {
            await context.RequireAsync(Key("parent"), ResourceDependencyLifetime.Build, token);
            return new("compiled");
        };
        fixture.Plan.Builds["scene"] = async (context, token) =>
        {
            await context.RequireAsync(Key("compiled"), cancellationToken: token);
            return new("scene");
        };
        using ResourceLease<TestValue> parent = await fixture.Manager.AcquireLeaseForTestAsync(Key("parent"));
        using ResourceLease<TestValue> scene = await fixture.Manager.AcquireLeaseForTestAsync(Key("scene"));
        fixture.Runtime.Set(new(3, true, 1));

        Assert.False(old.IsUsable);
        Assert.False(parent.IsUsable);
        Assert.Equal(1UL, Assert.Throws<ResourceUnavailableException>(() => parent.Value).Version);
        Assert.True(scene.IsUsable);
        Assert.Equal("scene", scene.Value.Label);
    }

    /// <summary>An already invalid runtime snapshot cannot be used while its change notification is still being delivered.</summary>
    /// <param name="access">The first resource access performed by the earlier change subscriber.</param>
    /// <returns>Completion of the ordered change notification and all availability checks.</returns>
    [Theory]
    [InlineData("lease")]
    [InlineData("value")]
    [InlineData("reference")]
    [InlineData("manager")]
    [InlineData("frame")]
    public async Task RuntimeInvalidationIsObservedBeforeManagerChangedCallbackAsync(string access)
    {
        ResourceLease<TestValue>? old = null;
        ResourceReference<TestValue>? owner = null;
        ResourceReadScope? frame = null;
        ResourceManager? manager = null;
        bool usable = true;
        Exception? valueError = null;
        await using Fixture fixture = new(
            runtime => runtime.Changed += (_, _) =>
            {
                ResourceLease<TestValue>? current = null;
                switch (access)
                {
                    case "lease":
                        usable = old!.IsUsable;
                        break;
                    case "value":
                        valueError = Record.Exception(() => old!.Value);
                        usable = valueError is null;
                        break;
                    case "reference":
                        usable = owner!.IsAvailable;
                        break;
                    case "manager":
                        usable = manager!.TryAcquireLeaseForTest(owner!.Handle, out current);
                        break;
                    case "frame":
                        usable = frame!.TryAcquire(owner!.Handle, out current);
                        break;
                }

                current?.Dispose();
            });
        fixture.Plan.Builds["value"] = (context, _) =>
        {
            context.TrackRuntimeDependency("runtime", fixture.Runtime.Snapshot.Version);
            return ValueTask.FromResult(new TestValue("value"));
        };
        manager = fixture.Manager;
        using (owner = await manager.AcquireAsync(Key("value")))
        using (old = await manager.AcquireLeaseForTestAsync(Key("value")))
        using (frame = manager.BeginRead())
        {
            fixture.Runtime.Set(new(2, false, 1));
            Assert.False(usable);
            if (access == "value")
            {
                Assert.IsType<ResourceUnavailableException>(valueError);
            }
        }
    }

    /// <summary>Invalid provider updates are reported by manager operations without escaping into host event callbacks.</summary>
    /// <param name="version">The malformed runtime version.</param>
    /// <param name="available">The malformed availability.</param>
    /// <param name="invalidated">The malformed invalidation watermark.</param>
    /// <returns>Completion of event delivery and manager rejection.</returns>
    [Theory]
    [InlineData(0UL, false, 0UL)]
    [InlineData(1UL, false, 0UL)]
    [InlineData(2UL, true, 2UL)]
    public async Task RuntimeContractViolationPoisonsOperationsWithoutThrowingFromEventAsync(ulong version, bool available, ulong invalidated)
    {
        await using Fixture fixture = new();
        fixture.Plan.Builds["value"] = (_, _) => ValueTask.FromResult(new TestValue("value"));
        using ResourceLease<TestValue> lease = await fixture.Manager.AcquireLeaseForTestAsync(Key("value"));

        Assert.Null(Record.Exception(() => fixture.Runtime.Set(new(version, available, invalidated))));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Manager.AcquireLeaseForTestAsync(Key("value")));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("runtime provider", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Repeated ownership operations allocate only their returned owner, without captured callbacks or temporary leases.</summary>
    /// <param name="operation">The ownership operation to measure.</param>
    /// <param name="maximumBytes">The allocation budget per operation, allowing object layout differences.</param>
    /// <returns>Completion of resource construction and the allocation measurement.</returns>
    [Theory]
    [InlineData("lease", 64)]
    [InlineData("reference", 128)]
    [InlineData("scope", 64)]
    public async Task WarmOwnershipOperationsHaveBoundedAllocationsAsync(string operation, int maximumBytes)
    {
        await using Fixture fixture = new(null, false);
        fixture.Plan.Builds["value"] = (_, _) => ValueTask.FromResult(new TestValue("value"));
        ResourceKey<TestValue> key = Key("value");
        using ResourceReference<TestValue> owner = await fixture.Manager.AcquireAsync(key);
        Action acquire = operation switch
        {
            "lease" => () => owner.Acquire().Dispose(),
            "reference" => () => fixture.Manager.AcquireAsync(key).GetAwaiter().GetResult().Dispose(),
            _ => () => fixture.Manager.BeginRead().Dispose(),
        };
        for (int i = 0; i < 10000; i++)
        {
            acquire();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            acquire();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 1, maximumBytes * 1000L);
        Assert.True(owner.IsAvailable);
    }

    /// <summary>Polling unavailable resources does not construct exceptions until a value is requested.</summary>
    /// <returns>Completion of runtime invalidation and the allocation measurement.</returns>
    [Fact]
    public async Task UnavailableLeaseAndViewChecksDoNotAllocateAsync()
    {
        await using Fixture fixture = new();
        fixture.Plan.Builds["value"] = (context, _) =>
        {
            context.TrackRuntimeDependency("runtime", fixture.Runtime.Snapshot.Version);
            return ValueTask.FromResult(new TestValue("value"));
        };
        using ResourceReference<TestValue> owner = await fixture.Manager.AcquireAsync(Key("value"));
        ResourceHandle<TestValue> handle = owner.Handle;
        using ResourceLease<TestValue> lease = owner.Acquire();
        using ResourceReadScope scope = fixture.Manager.BeginRead();
        fixture.Runtime.Set(new(2, false, 1));
        bool available = false;
        for (int pass = 0; pass < 2; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                available |= lease.IsUsable | owner.IsAvailable;
                available |= scope.TryAcquire(handle, out ResourceLease<TestValue>? current);
                current?.Dispose();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (pass == 1)
            {
                Assert.Equal(0, allocated);
            }
        }

        Assert.False(available);
        ResourceUnavailableException error = Assert.Throws<ResourceUnavailableException>(() => lease.Value);
        Assert.Equal(1UL, error.Version);
    }

    private static ResourceKey<TestValue> Key(string selector) => new("asset", selector);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        internal Fixture()
            : this(null)
        {
        }

        internal Fixture(Action<RuntimeState>? beforeManager, bool registerRuntime = true)
        {
            ServiceCollection services = new();
            services.AddSingleton(Plan);
            services.AddSingleton(Runtime);
            services.AddMemoryManagement();
            services.AddResources("test", builder =>
            {
                builder.UseSource<TestSource>();
                if (registerRuntime)
                {
                    builder.UseRuntimeDependency<TestRuntime>("runtime");
                }

                builder.AddSourcePipeline<TestValue, TestStage>();
            });
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _scope = _provider.CreateAsyncScope();
            beforeManager?.Invoke(Runtime);
            Manager = _scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("test");
        }

        internal BuildPlan Plan { get; } = new();

        internal RuntimeState Runtime { get; } = new();

        internal ResourceManager Manager { get; }

        public async ValueTask DisposeAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            while (true)
            {
                try
                {
                    await Manager.DisposeAsync();
                    break;
                }
                catch (InvalidOperationException) when (!timeout.IsCancellationRequested)
                {
                    // Cancelled shared loads can finish their manager-owned cleanup after their waiters leave.
                    await Task.Delay(1, timeout.Token);
                }
            }

            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class BuildPlan
    {
        internal ConcurrentDictionary<string, Func<ResourceBuildContext, CancellationToken, ValueTask<TestValue>>> Builds { get; } = new();

        internal Func<TestValue, ValueTask>? Release { get; set; }

        internal int SourceRevision { get; set; } = 1;
    }

    private sealed class TestValue(string label)
    {
        internal string Label { get; } = label;

        internal bool Released { get; set; }
    }

    private sealed class TestStage(BuildPlan plan) : IResourceSourceStage<TestValue>
    {
        public ValueTask<TestValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => plan.Builds[context.Address.Subresource](context, cancellationToken);

        public async ValueTask ReleaseAsync(TestValue resource)
        {
            Assert.False(resource.Released);
            if (plan.Release is { } release)
            {
                await release(resource);
            }

            resource.Released = true;
        }
    }

    private sealed class TestSource(BuildPlan plan) : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, plan.SourceRevision.ToString(CultureInfo.InvariantCulture)));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(plan.SourceRevision.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class Snapshot(ResourceId id, string revision) : ResourceSourceSnapshot
    {
        public override ResourceId Id { get; } = id;

        public override string Revision { get; } = revision;

        public override long? Length => 0;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream());

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RuntimeState
    {
        internal event EventHandler? Changed;

        internal ResourceRuntimeSnapshot Snapshot { get; private set; } = new(1, true, 0);

        internal void Set(ResourceRuntimeSnapshot snapshot)
        {
            Snapshot = snapshot;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestRuntime(RuntimeState state) : IResourceRuntimeDependency
    {
        public event EventHandler? Changed
        {
            add => state.Changed += value;
            remove => state.Changed -= value;
        }

        public string Id => "runtime";

        public ResourceRuntimeSnapshot Snapshot => state.Snapshot;
    }
}
