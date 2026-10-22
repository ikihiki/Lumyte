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

    /// <summary>Reload replaces every forced node's old outgoing edges before checking the replacement graph.</summary>
    /// <returns>Completion of a valid dependency reversal.</returns>
    [Fact]
    public async Task ReloadAllowsAcyclicDependencyDirectionToReverseAsync()
    {
        await using Fixture fixture = new();
        fixture.Plan.Builds["first"] = async (context, token) =>
        {
            ResourceSourceSnapshot source = await context.OpenSourceAsync(context.Address.Id, cancellationToken: token);
            if (source.Revision == "2")
            {
                await context.RequireAsync(Key("second"), cancellationToken: token);
            }

            return new($"first:{source.Revision}");
        };
        fixture.Plan.Builds["second"] = async (context, token) =>
        {
            ResourceSourceSnapshot source = await context.OpenSourceAsync(context.Address.Id, cancellationToken: token);
            if (source.Revision == "1")
            {
                await context.RequireAsync(Key("first"), cancellationToken: token);
            }

            return new($"second:{source.Revision}");
        };
        using ResourceReference<TestValue> first = await fixture.Manager.AcquireAsync(Key("first"));
        using ResourceReference<TestValue> second = await fixture.Manager.AcquireAsync(Key("second"));
        fixture.Plan.SourceRevision = 2;

        ResourceReloadResult result = await fixture.Manager.ReloadForTestAsync([new("asset")]);
        Assert.Equal(ResourceReloadStatus.Published, result.Status);
        Assert.Equal(2, result.UpdatedResources);
        using ResourceLease<TestValue> firstValue = await fixture.Manager.AcquireLeaseForTestAsync(Key("first"));
        using ResourceLease<TestValue> secondValue = await fixture.Manager.AcquireLeaseForTestAsync(Key("second"));
        Assert.Equal("first:2", firstValue.Value.Label);
        Assert.Equal("second:2", secondValue.Value.Label);
    }

    private static ResourceKey<TestValue> Key(string selector) => new("asset", selector);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        internal Fixture()
        {
            ServiceCollection services = new();
            services.AddSingleton(Plan);
            services.AddMemoryManagement();
            services.AddResources("test", builder =>
            {
                builder.UseSource<TestSource>();
                builder.AddSourcePipeline<TestValue, TestStage>();
            });
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _scope = _provider.CreateAsyncScope();
            Manager = _scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("test");
        }

        internal BuildPlan Plan { get; } = new();

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
}
