using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Exercises resource ownership, dependency graphs and generation publication through public contracts.</summary>
public sealed class ResourceManagerTests
{
    private static readonly MemoryPoolId _cpu = new("cpu");

    /// <summary>Typed transforms share one source and release build inputs without releasing the completed output.</summary>
    /// <returns>Completion of the pipeline and ownership checks.</returns>
    [Fact]
    public async Task MultiStagePipelineReleasesBuildInputsAsync()
    {
        await using Fixture fixture = new(builder => builder
            .AddSourcePipeline<DocumentValue, DocumentStage>("document")
            .AddTransformPipeline<DocumentValue, CpuValue, CpuStage>("cpu", "document")
            .AddTransformPipeline<CpuValue, TestValue, UploadStage>("gpu", "cpu"));
        fixture.Source.Set("asset", "encoded");

        using ResourceLease<TestValue> first = await fixture.Manager.AcquireAsync(Key("gpu"));
        using ResourceLease<TestValue> second = await fixture.Manager.AcquireAsync(Key("gpu"));
        Assert.Same(first.Value, second.Value);
        Assert.Equal("uploaded:decoded:encoded", first.Value.Text);
        Assert.Equal(1, fixture.Source.OpenCount);
        Assert.Equal(2, await fixture.Manager.TrimAsync());
        Assert.Equal(["cpu", "document"], fixture.Plan.Released.Order());
        Assert.False(first.Value.Released);
    }

    /// <summary>A resident diamond shares its leaf and releases every parent before the leaf.</summary>
    /// <returns>Completion of dependency sharing and release.</returns>
    [Fact]
    public async Task ResidentDiamondSharesLeafAndTrimsParentsFirstAsync()
    {
        await using Fixture fixture = new(builder => builder
            .AddSourcePipeline<TestValue, PlannedStage>("root")
            .AddSourcePipeline<TestValue, PlannedStage>("left")
            .AddSourcePipeline<TestValue, PlannedStage>("right")
            .AddSourcePipeline<TestValue, PlannedStage>("leaf"));
        fixture.Plan.Builds["leaf"] = (_, _) => ValueTask.FromResult(new TestValue("leaf"));
        fixture.Plan.Builds["left"] = async (context, token) =>
            new("left", await context.RequireAsync(Key("leaf"), cancellationToken: token));
        fixture.Plan.Builds["right"] = async (context, token) =>
            new("right", await context.RequireAsync(Key("leaf"), cancellationToken: token));
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            TestValue left = await context.RequireAsync(Key("left"), cancellationToken: token);
            TestValue right = await context.RequireAsync(Key("right"), cancellationToken: token);
            Assert.Same(left.Dependency, right.Dependency);
            return new("root", left);
        };

        ResourceLease<TestValue> root = await fixture.Manager.AcquireAsync(Key("root"));
        Assert.Equal(1, fixture.Plan.BuildCounts["leaf"]);
        Assert.Equal(0, await fixture.Manager.TrimAsync());
        root.Dispose();
        Assert.Equal(4, await fixture.Manager.TrimAsync());
        string[] released = fixture.Plan.Released.ToArray();
        Assert.Equal("root", released[0]);
        Assert.Equal("leaf", released[^1]);
        Assert.Equal(4, released.Distinct().Count());
    }

    /// <summary>A stage cannot claim a borrowed dependency as its separately owned output.</summary>
    /// <returns>Completion of rejected alias publication and the original owner's release.</returns>
    [Fact]
    public async Task BorrowedDependencyCannotBecomeAnotherOwnedOutputAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("root", "leaf");
        fixture.Plan.Builds["leaf"] = (_, _) => ValueTask.FromResult(new TestValue("leaf"));
        fixture.Plan.Builds["root"] = (context, token) => context.RequireAsync(Key("leaf"), cancellationToken: token);
        ResourceLease<TestValue> leaf = await fixture.Manager.AcquireAsync(Key("leaf"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.AcquireAsync(Key("root")));
        Assert.False(leaf.Value.Released);
        Assert.Empty(fixture.Plan.Released);
        leaf.Dispose();
        Assert.Equal(1, await fixture.Manager.TrimAsync());
        Assert.Equal(["leaf"], fixture.Plan.Released);
    }

    /// <summary>Selectors and decode variants use independent outputs while sharing a normalized document input.</summary>
    /// <returns>Completion of document projection.</returns>
    [Fact]
    public async Task DocumentProjectionsNormalizeInputAddressAsync()
    {
        await using Fixture fixture = new(builder => builder
            .AddSourcePipeline<DocumentValue, DocumentStage>("document")
            .AddTransformPipeline<DocumentValue, CpuValue, MeshProjection>("mesh", "document")
            .AddTransformPipeline<DocumentValue, ImageValue, ImageProjection>("image", "document"));
        fixture.Source.Set("asset", "document bytes");
        ResourceAddress meshAddress = new(new("asset"), "mesh/0", "tangent");
        ResourceAddress otherVariant = new(new("asset"), "mesh/0", "position");
        ResourceAddress imageAddress = new(new("asset"), "image/1", "linear");
        using ResourceLease<CpuValue> mesh = await fixture.Manager.AcquireAsync(new ResourceKey<CpuValue>(meshAddress, "mesh"));
        using ResourceLease<CpuValue> other = await fixture.Manager.AcquireAsync(new ResourceKey<CpuValue>(otherVariant, "mesh"));
        using ResourceLease<ImageValue> image = await fixture.Manager.AcquireAsync(new ResourceKey<ImageValue>(imageAddress, "image"));

        Assert.Equal("mesh/0:tangent", mesh.Value.Text);
        Assert.Equal("mesh/0:position", other.Value.Text);
        Assert.Equal("image/1:linear", image.Value.Text);
        Assert.NotSame(mesh.Value, other.Value);
        Assert.Same(mesh.Value.Document, other.Value.Document);
        Assert.Same(mesh.Value.Document, image.Value.Document);
        Assert.Equal(1, fixture.Source.OpenCount);
        Assert.Equal(2, mesh.Value.Document!.Subresources.Count);
        Assert.Equal(0, await fixture.Manager.TrimAsync());
    }

    /// <summary>One cancelled caller leaves a shared asynchronous load running for the remaining caller.</summary>
    /// <returns>Completion of both callers.</returns>
    [Fact]
    public async Task ConcurrentAcquireSharesBuildAndIsolatesCallerCancellationAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("value");
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource resume = NewSignal();
        fixture.Plan.Builds["value"] = async (_, token) =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(token);
            return new("value");
        };
        using CancellationTokenSource cancellation = new();
        Task<ResourceLease<TestValue>> cancelled = fixture.Manager.AcquireAsync(Key("value"), cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<ResourceLease<TestValue>> surviving = fixture.Manager.AcquireAsync(Key("value")).AsTask();
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
            Assert.False(surviving.IsCompleted);
            Assert.Equal(1, fixture.Plan.BuildCounts["value"]);
        }
        finally
        {
            resume.TrySetResult();
        }

        using ResourceLease<TestValue> lease = await surviving.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceLease<TestValue> cached = await fixture.Manager.AcquireAsync(Key("value"));
        Assert.Same(lease.Value, cached.Value);
        Assert.Equal(1, fixture.Plan.BuildCounts["value"]);
    }

    /// <summary>Dependencies between separately started builds fail with a cycle instead of deadlocking.</summary>
    /// <returns>Completion of both failed builds.</returns>
    [Fact]
    public async Task IndependentBuildTasksDetectDynamicCycleAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("left", "right");
        TaskCompletionSource bothStarted = NewSignal();
        int started = 0;
        async ValueTask<TestValue> BuildAsync(ResourceBuildContext context, CancellationToken token)
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.SetResult();
            }

            await bothStarted.Task.WaitAsync(token);
            string dependency = context.PipelineId == "left" ? "right" : "left";
            return new(context.PipelineId, await context.RequireAsync(Key(dependency), cancellationToken: token));
        }

        fixture.Plan.Builds["left"] = BuildAsync;
        fixture.Plan.Builds["right"] = BuildAsync;
        Task<ResourceLease<TestValue>> left = fixture.Manager.AcquireAsync(Key("left")).AsTask();
        Task<ResourceLease<TestValue>> right = fixture.Manager.AcquireAsync(Key("right")).AsTask();
        Exception? failure = await Record.ExceptionAsync(async () =>
            await Task.WhenAll(left, right).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotNull(failure);
        Assert.IsNotType<TimeoutException>(failure);
        Assert.Contains("left", failure.ToString(), StringComparison.Ordinal);
        Assert.Contains("right", failure.ToString(), StringComparison.Ordinal);
        Assert.True(left.IsFaulted);
        Assert.True(right.IsFaulted);
    }

    /// <summary>A failed release keeps resident memory and dependencies until a successful retry.</summary>
    /// <returns>Completion of the failed and retried trim.</returns>
    [Fact]
    public async Task ReleaseFailureRetainsMemoryAndDependenciesForRetryAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("root", "leaf");
        fixture.Plan.Builds["leaf"] = (_, _) => ValueTask.FromResult(new TestValue("leaf"));
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            await context.GetMemoryScope(ResourceDependencyLifetime.Resident)
                .RentAsync<byte>(_cpu, 12, MemoryPurpose.Resident, token);
            return new("root", await context.RequireAsync(Key("leaf"), cancellationToken: token));
        };
        int attempts = 0;
        fixture.Plan.Release = value =>
        {
            if (value.Text == "root" && Interlocked.Increment(ref attempts) == 1)
            {
                throw new IOException("release not yet safe");
            }

            return ValueTask.CompletedTask;
        };

        ResourceLease<TestValue> lease = await fixture.Manager.AcquireAsync(Key("root"));
        lease.Dispose();
        await Assert.ThrowsAsync<AggregateException>(async () => await fixture.Manager.TrimAsync());
        Assert.Empty(fixture.Plan.Released);
        Assert.True(fixture.Memory.GetSnapshot(_cpu).CommittedBytes > 0);
        Assert.Equal(2, await fixture.Manager.TrimAsync());
        Assert.Equal(["root", "leaf"], fixture.Plan.Released);
        Assert.Equal(0UL, fixture.Memory.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(2, attempts);
    }

    /// <summary>Repeated dependency requests share one value and promote its lifetime to resident.</summary>
    /// <returns>Completion of lifetime promotion and final release.</returns>
    [Fact]
    public async Task RequiringBuildThenResidentPromotesOneSharedDependencyAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("root", "leaf");
        fixture.Plan.Builds["leaf"] = (_, _) => ValueTask.FromResult(new TestValue("leaf"));
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            TestValue build = await context.RequireAsync(Key("leaf"), ResourceDependencyLifetime.Build, token);
            TestValue resident = await context.RequireAsync(Key("leaf"), ResourceDependencyLifetime.Resident, token);
            Assert.Same(build, resident);
            return new("root", resident);
        };
        ResourceLease<TestValue> root = await fixture.Manager.AcquireAsync(Key("root"));
        Assert.Equal(0, await fixture.Manager.TrimAsync());
        Assert.Equal(1, fixture.Plan.BuildCounts["leaf"]);
        Assert.False(root.Value.Dependency!.Released);
        root.Dispose();
        Assert.Equal(2, await fixture.Manager.TrimAsync());
        Assert.Equal(["root", "leaf"], fixture.Plan.Released);
    }

    /// <summary>Failed build cleanup keeps its memory charged and does not repeat previously successful release work.</summary>
    /// <returns>Completion of failed publication, blocked rebuild and cleanup recovery.</returns>
    [Fact]
    public async Task FailedBuildCleanupRetainsChargeAndRetriesOnlyUnfinishedWorkAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("value");
        bool cleanupBlocked = true;
        int completedCleanupCalls = 0;
        fixture.Plan.Builds["value"] = async (context, token) =>
        {
            await context.GetMemoryScope(ResourceDependencyLifetime.Build)
                .RentAsync<byte>(_cpu, 12, MemoryPurpose.Build, token);
            context.RegisterCleanup(() =>
            {
                if (cleanupBlocked)
                {
                    throw new IOException("cleanup not yet safe");
                }

                return ValueTask.CompletedTask;
            });
            context.RegisterCleanup(() =>
            {
                completedCleanupCalls++;
                return ValueTask.CompletedTask;
            });
            return new("value");
        };

        await Assert.ThrowsAsync<AggregateException>(async () => await fixture.Manager.AcquireAsync(Key("value")));
        Assert.Equal(1, completedCleanupCalls);
        Assert.Equal(["value"], fixture.Plan.Released);
        Assert.True(fixture.Memory.GetSnapshot(_cpu).CommittedBytes > 0);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.AcquireAsync(Key("value")));
        Assert.Equal(1, fixture.Plan.BuildCounts["value"]);

        cleanupBlocked = false;
        Assert.Equal(1, await fixture.Manager.TrimAsync());
        Assert.Equal(1, completedCleanupCalls);
        Assert.Equal(["value"], fixture.Plan.Released);
        Assert.Equal(0UL, fixture.Memory.GetSnapshot(_cpu).CommittedBytes);
        using ResourceLease<TestValue> retry = await fixture.Manager.AcquireAsync(Key("value"));
        Assert.Equal(2, fixture.Plan.BuildCounts["value"]);
        Assert.False(retry.Value.Released);
    }

    /// <summary>A handle is unmanaged and non-owning, and an expired generation cannot resolve after reuse.</summary>
    /// <returns>Completion of the handle ownership checks.</returns>
    [Fact]
    public async Task HandlesAreUnmanagedAndRejectExpiredOrForeignOwnersAsync()
    {
        Assert.Equal(16, UnmanagedSize<ResourceHandle<TestValue>>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ResourceHandle<TestValue>>());
        await using Fixture fixture = CreatePlannedFixture("value");
        await using Fixture other = CreatePlannedFixture("value");
        fixture.Plan.Builds["value"] = (_, _) => ValueTask.FromResult(new TestValue("value"));
        using ResourceReference<TestValue> owner = await fixture.Manager.CreateReferenceAsync(Key("value"));
        ResourceHandle<TestValue> handle = owner.Handle;
        ResourceHandle<TestValue> copy = handle;
        ResourceHandle<CpuValue> wrongType = Unsafe.As<ResourceHandle<TestValue>, ResourceHandle<CpuValue>>(ref copy);
        using ResourceReadScope captured = fixture.Manager.BeginRead();
        Assert.False(handle.IsDefault);
        Assert.False(other.Manager.TryAcquire(copy, out ResourceLease<TestValue>? foreign));
        Assert.Null(foreign);
        Assert.False(fixture.Manager.TryAcquire(default(ResourceHandle<TestValue>), out ResourceLease<TestValue>? empty));
        Assert.Null(empty);
        Assert.False(fixture.Manager.TryAcquire(wrongType, out ResourceLease<CpuValue>? mismatched));
        Assert.Null(mismatched);
        Assert.True(fixture.Manager.TryAcquire(handle, out ResourceLease<TestValue>? lease));
        using (lease)
        {
            owner.Dispose();
            Assert.False(fixture.Manager.TryAcquire(copy, out ResourceLease<TestValue>? expired));
            Assert.Null(expired);
            Assert.False(captured.TryAcquire(copy, out expired));
            Assert.Null(expired);
            Assert.Equal("value", lease!.Value.Text);
            using ResourceReference<TestValue> replacement = await fixture.Manager.CreateReferenceAsync(Key("value"));
            Assert.NotEqual(handle, replacement.Handle);
            Assert.False(fixture.Manager.TryAcquire(handle, out expired));
            Assert.True(fixture.Manager.TryAcquire(replacement.Handle, out ResourceLease<TestValue>? current));
            current!.Dispose();
        }
    }

    /// <summary>Reload publishes all affected outputs together and captured read scopes keep the earlier map.</summary>
    /// <returns>Completion of atomic reload and old-generation ownership checks.</returns>
    [Fact]
    public async Task ReloadPublishesAtomicallyAndKeepsOldLeasesAndReadScopesAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("root", "leaf");
        fixture.Source.Set("asset", "old");
        TaskCompletionSource candidateReady = NewSignal();
        TaskCompletionSource publish = NewSignal();
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            TestValue input = await context.RequireAsync(Key("leaf"), cancellationToken: token);
            if (input.Text == "new")
            {
                candidateReady.SetResult();
                await publish.Task.WaitAsync(token);
            }

            return new(input.Text, input);
        };
        using ResourceReference<TestValue> root = await fixture.Manager.CreateReferenceAsync(Key("root"));
        using ResourceReference<TestValue> leaf = await fixture.Manager.CreateReferenceAsync(Key("leaf"));
        using ResourceReadScope before = fixture.Manager.BeginRead();
        using ResourceLease<TestValue> oldLease = await fixture.Manager.AcquireAsync(Key("root"));
        ResourceHandle<TestValue> handle = root.Handle;
        ulong version = fixture.Manager.PublicationVersion;
        fixture.Source.Set("asset", "new");
        Task<ResourceReloadResult> reload = fixture.Manager.ReloadAsync([new("asset")]).AsTask();
        await candidateReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            AssertHandleText(fixture.Manager, root.Handle, "old");
            AssertHandleText(fixture.Manager, leaf.Handle, "old");
            Assert.Equal(version, fixture.Manager.PublicationVersion);
        }
        finally
        {
            publish.TrySetResult();
        }

        ResourceReloadResult result = await reload.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ResourceReloadStatus.Published, result.Status);
        Assert.Equal(version + 1, result.PublicationVersion);
        Assert.Equal(handle, root.Handle);
        AssertHandleText(fixture.Manager, root.Handle, "new");
        AssertHandleText(fixture.Manager, leaf.Handle, "new");
        Assert.Equal("old", oldLease.Value.Text);
        Assert.True(before.TryAcquire(root.Handle, out ResourceLease<TestValue>? capturedRoot));
        Assert.True(before.TryAcquire(leaf.Handle, out ResourceLease<TestValue>? capturedLeaf));
        using (capturedRoot)
        using (capturedLeaf)
        {
            Assert.Equal("old", capturedRoot!.Value.Text);
            Assert.Equal("old", capturedLeaf!.Value.Text);
        }

        await fixture.Manager.TrimAsync();
        Assert.False(oldLease.Value.Released);
    }

    /// <summary>Failure in an affected parent preserves the previously published graph and version.</summary>
    /// <returns>Completion of the rejected reload.</returns>
    [Fact]
    public async Task FailedReloadPreservesPublishedGraphAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("root", "leaf");
        fixture.Source.Set("asset", "old");
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            TestValue input = await context.RequireAsync(Key("leaf"), cancellationToken: token);
            if (input.Text == "broken")
            {
                throw new IOException("invalid source");
            }

            return new(input.Text, input);
        };
        using ResourceReference<TestValue> root = await fixture.Manager.CreateReferenceAsync(Key("root"));
        using ResourceReference<TestValue> leaf = await fixture.Manager.CreateReferenceAsync(Key("leaf"));
        ulong version = fixture.Manager.PublicationVersion;
        fixture.Source.Set("asset", "broken");

        await Assert.ThrowsAsync<IOException>(async () => await fixture.Manager.ReloadAsync([new("asset")]));
        Assert.Equal(version, fixture.Manager.PublicationVersion);
        AssertHandleText(fixture.Manager, root.Handle, "old");
        AssertHandleText(fixture.Manager, leaf.Handle, "old");
    }

    /// <summary>A source change while a candidate is built rejects publication and retains the old graph.</summary>
    /// <returns>Completion of the conflicted reload.</returns>
    [Fact]
    public async Task ReloadRejectsSourceRevisionChangedDuringConstructionAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("root", "leaf");
        fixture.Source.Set("asset", "old");
        TaskCompletionSource candidateReady = NewSignal();
        TaskCompletionSource finish = NewSignal();
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            TestValue input = await context.RequireAsync(Key("leaf"), cancellationToken: token);
            if (input.Text == "candidate")
            {
                candidateReady.SetResult();
                await finish.Task.WaitAsync(token);
            }

            return new(input.Text, input);
        };
        using ResourceReference<TestValue> owner = await fixture.Manager.CreateReferenceAsync(Key("root"));
        ulong version = fixture.Manager.PublicationVersion;
        fixture.Source.Set("asset", "candidate");
        Task<ResourceReloadResult> reload = fixture.Manager.ReloadAsync([new("asset")]).AsTask();
        await candidateReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source.Set("asset", "superseding");
        finish.SetResult();

        ResourceReloadResult result = await reload.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ResourceReloadStatus.Conflict, result.Status);
        Assert.Equal(0, result.UpdatedResources);
        Assert.Equal(version, fixture.Manager.PublicationVersion);
        AssertHandleText(fixture.Manager, owner.Handle, "old");
    }

    /// <summary>Source provenance survives releasing a build input and still selects its parent for reload.</summary>
    /// <param name="rescan">Whether to recheck all tracked sources instead of a specific identifier.</param>
    /// <returns>Completion of reload after intermediate trimming.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReloadTracksTrimmedBuildDependencyAsync(bool rescan)
    {
        await using Fixture fixture = CreatePlannedFixture("root", "leaf");
        fixture.Source.Set("asset", "old");
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            TestValue input = await context.RequireAsync(Key("leaf"), ResourceDependencyLifetime.Build, token);
            return new(input.Text);
        };
        using ResourceReference<TestValue> owner = await fixture.Manager.CreateReferenceAsync(Key("root"));
        Assert.Equal(1, await fixture.Manager.TrimAsync());
        Assert.Equal(1, fixture.Plan.BuildCounts["leaf"]);
        fixture.Source.Set("asset", "new");

        ResourceReloadResult result = rescan
            ? await fixture.Manager.ReloadAllAsync()
            : await fixture.Manager.ReloadAsync([new("asset")]);
        Assert.Equal(ResourceReloadStatus.Published, result.Status);
        AssertHandleText(fixture.Manager, owner.Handle, "new");
        Assert.Equal(2, fixture.Plan.BuildCounts["root"]);
        Assert.Equal(2, fixture.Plan.BuildCounts["leaf"]);
    }

    /// <summary>Runtime invalidation propagates through resident edges while independent build results remain usable.</summary>
    /// <returns>Completion of runtime loss and recovery.</returns>
    [Fact]
    public async Task RuntimeLossInvalidatesResidentsAndRebuildsExistingHandleAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("gpu", "scene", "cpu");
        fixture.Plan.Builds["gpu"] = (context, _) =>
        {
            context.TrackRuntimeDependency("device", fixture.Runtime.Snapshot.Version);
            return ValueTask.FromResult(new TestValue($"gpu:{fixture.Runtime.Snapshot.Version}"));
        };
        fixture.Plan.Builds["scene"] = async (context, token) =>
            new("scene", await context.RequireAsync(Key("gpu"), cancellationToken: token));
        fixture.Plan.Builds["cpu"] = (context, _) =>
        {
            context.TrackRuntimeDependency("device", fixture.Runtime.Snapshot.Version, ResourceDependencyLifetime.Build);
            return ValueTask.FromResult(new TestValue("cpu"));
        };
        using ResourceReference<TestValue> gpu = await fixture.Manager.CreateReferenceAsync(Key("gpu"));
        using ResourceReference<TestValue> scene = await fixture.Manager.CreateReferenceAsync(Key("scene"));
        using ResourceReference<TestValue> cpu = await fixture.Manager.CreateReferenceAsync(Key("cpu"));
        using ResourceLease<TestValue> oldGpu = await fixture.Manager.AcquireAsync(Key("gpu"));
        using ResourceReadScope oldFrame = fixture.Manager.BeginRead();
        ResourceHandle<TestValue> handle = gpu.Handle;
        fixture.Runtime.Set(new(2, false, 1));

        Assert.False(gpu.IsAvailable);
        Assert.False(scene.IsAvailable);
        Assert.False(oldGpu.IsUsable);
        Assert.Throws<ResourceUnavailableException>(() => oldGpu.Value);
        Assert.False(fixture.Manager.TryAcquire(handle, out ResourceLease<TestValue>? invalid));
        Assert.Null(invalid);
        Assert.False(oldFrame.TryAcquire(handle, out invalid));
        AssertHandleText(fixture.Manager, cpu.Handle, "cpu");
        await Assert.ThrowsAsync<ResourceUnavailableException>(async () => await fixture.Manager.AcquireAsync(Key("gpu")));

        fixture.Runtime.Set(new(3, true, 1));
        ResourceReloadResult rebuilt = await fixture.Manager.RebuildRuntimeAsync(["device"]);
        Assert.Equal(ResourceReloadStatus.Published, rebuilt.Status);
        Assert.Equal(handle, gpu.Handle);
        Assert.True(scene.IsAvailable);
        AssertHandleText(fixture.Manager, handle, "gpu:3");
        Assert.False(oldGpu.IsUsable);
        Assert.False(oldFrame.TryAcquire(handle, out invalid));
    }

    /// <summary>Shutdown rejects live external owners without changing manager state, and succeeds after they leave.</summary>
    /// <returns>Completion of the shutdown retries.</returns>
    [Fact]
    public async Task ShutdownRejectsReferencesLeasesAndReadScopesAsync()
    {
        await using Fixture fixture = CreatePlannedFixture("value");
        fixture.Plan.Builds["value"] = (_, _) => ValueTask.FromResult(new TestValue("value"));
        ResourceReference<TestValue> owner = await fixture.Manager.CreateReferenceAsync(Key("value"));
        ResourceLease<TestValue> lease = await fixture.Manager.AcquireAsync(Key("value"));
        ResourceReadScope read = fixture.Manager.BeginRead();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.DisposeAsync());
        owner.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.DisposeAsync());
        lease.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.DisposeAsync());
        read.Dispose();
        await fixture.Manager.DisposeAsync();
        await fixture.Manager.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await fixture.Manager.AcquireAsync(Key("value")));
    }

    /// <summary>The DI scope owns distinct source and stage instances and disposes each service once after its managers.</summary>
    /// <returns>Completion of both managers and their DI scope.</returns>
    [Fact]
    public async Task DependencyInjectionSeparatesManagersAndPipelinesAndOwnsServicesAsync()
    {
        ServiceCollection services = new();
        ServiceCounts counts = new();
        services.AddSingleton(counts);
        services.AddMemoryManagement();
        foreach (string name in new[] { "first", "second" })
        {
            services.AddResources(name, builder => builder.UseSource<CountingSource>()
                .AddSourcePipeline<TestValue, CountingStage>("one")
                .AddSourcePipeline<TestValue, CountingStage>("two"));
        }

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager first = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("first");
        ResourceManager second = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("second");
        Assert.NotEqual(first.ManagerId, second.ManagerId);
        Assert.Same(first, scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("first"));
        using (ResourceLease<TestValue> one = await first.AcquireAsync(Key("one")))
        using (ResourceLease<TestValue> two = await first.AcquireAsync(Key("two")))
        using (ResourceLease<TestValue> other = await second.AcquireAsync(Key("one")))
        {
            Assert.NotEqual(one.Value.Text, two.Value.Text);
            Assert.NotEqual(one.Value.Text, other.Value.Text);
        }

        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.Equal(0, counts.SourceDisposals);
        Assert.Equal(0, counts.StageDisposals);
        Assert.Equal(2, counts.SourceInstances);
        Assert.Equal(4, counts.StageInstances);
        await scope.DisposeAsync();
        Assert.Equal(2, counts.SourceDisposals);
        Assert.Equal(4, counts.StageDisposals);
    }

    /// <summary>Duplicate registrations and mutation after configuration fail before manager use.</summary>
    [Fact]
    public void DependencyInjectionRejectsDuplicateAndLateConfiguration()
    {
        ServiceCollection services = new();
        ResourceRegistrationBuilder? captured = null;
        services.AddResources("main", builder =>
        {
            captured = builder;
            builder.UseSource<CountingSource>().AddSourcePipeline<TestValue, CountingStage>("value");
            Assert.Throws<InvalidOperationException>(() => builder.UseSource<CountingSource>());
            Assert.Throws<ArgumentException>(() => builder.AddSourcePipeline<TestValue, CountingStage>("value"));
        });
        Assert.Throws<ArgumentException>(() => services.AddResources("main", _ => { }));
        Assert.Throws<InvalidOperationException>(() => captured!.AddSourcePipeline<TestValue, CountingStage>("late"));
        Assert.Throws<NotSupportedException>(() => new ServiceCollection().AddResources("singleton", builder =>
            builder.UseSource<CountingSource>(ServiceLifetime.Singleton)));
        Assert.Throws<NotSupportedException>(() => new ServiceCollection().AddResources("transient", builder =>
            builder.UseSource<CountingSource>().AddSourcePipeline<TestValue, CountingStage>("value", ServiceLifetime.Transient)));
    }

    /// <summary>Manager resolution rejects a missing or incorrectly typed static transform input.</summary>
    /// <param name="missing">Whether the input is absent instead of having the wrong output type.</param>
    /// <returns>Completion of service provider disposal.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DependencyInjectionRejectsInvalidTransformInputAsync(bool missing)
    {
        ServiceCollection services = new();
        services.AddSingleton(new BuildPlan());
        services.AddSingleton(new SourceStore());
        services.AddMemoryManagement();
        services.AddResources("invalid", builder =>
        {
            builder.UseSource<FakeSource>().AddTransformPipeline<DocumentValue, CpuValue, CpuStage>("cpu", "input");
            if (!missing)
            {
                builder.AddSourcePipeline<TestValue, PlannedStage>("input");
            }
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.ThrowsAny<ArgumentException>(() => scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("invalid"));
    }

    /// <summary>Runtime provider IDs must match the manager registration.</summary>
    /// <returns>Completion of service provider disposal.</returns>
    [Fact]
    public async Task DependencyInjectionRejectsMismatchedRuntimeIdentifierAsync()
    {
        ServiceCollection services = new();
        services.AddSingleton(new BuildPlan());
        services.AddSingleton(new SourceStore());
        services.AddSingleton(new RuntimeState());
        services.AddMemoryManagement();
        services.AddResources("invalid", builder => builder.UseSource<FakeSource>()
            .AddSourcePipeline<TestValue, PlannedStage>("value").UseRuntimeDependency<FakeRuntime>("other"));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("invalid"));
    }

    /// <summary>Logical source identifiers cannot escape their source or embed platform-specific addressing.</summary>
    /// <param name="value">The rejected identifier.</param>
    [Theory]
    [InlineData("")]
    [InlineData("/asset")]
    [InlineData("asset/")]
    [InlineData("asset//mesh")]
    [InlineData("./asset")]
    [InlineData("asset/../mesh")]
    [InlineData("asset\\mesh")]
    [InlineData("asset\nmesh")]
    [InlineData("asset:mesh")]
    [InlineData("asset?mesh")]
    [InlineData("asset#mesh")]
    public void ResourceIdRejectsInvalidLogicalNames(string value)
        => Assert.Throws<ArgumentException>(() => new ResourceId(value));

    /// <summary>Identifier equality remains ordinal without case folding or Unicode normalization.</summary>
    [Fact]
    public void ResourceIdPreservesOrdinalIdentity()
    {
        Assert.NotEqual(new ResourceId("Asset"), new ResourceId("asset"));
        Assert.NotEqual(new ResourceId("caf\u00e9"), new ResourceId("cafe\u0301"));
        Assert.Equal("images/%20", new ResourceId("images/%20").Value);
    }

    private static Fixture CreatePlannedFixture(params string[] pipelines) => new(builder =>
    {
        foreach (string pipeline in pipelines)
        {
            builder.AddSourcePipeline<TestValue, PlannedStage>(pipeline);
        }
    });

    private static ResourceKey<TestValue> Key(string pipeline) => new(new(new("asset")), pipeline);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int UnmanagedSize<T>()
        where T : unmanaged => Unsafe.SizeOf<T>();

    private static void AssertHandleText(ResourceManager manager, ResourceHandle<TestValue> handle, string expected)
    {
        Assert.True(manager.TryAcquire(handle, out ResourceLease<TestValue>? lease));
        using (lease)
        {
            Assert.Equal(expected, lease!.Value.Text);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        internal Fixture(Action<ResourceRegistrationBuilder> configure)
        {
            ServiceCollection services = new();
            services.AddSingleton(Plan);
            services.AddSingleton(Source);
            services.AddSingleton(Runtime);
            services.AddMemoryManagement(options =>
            {
                options.ChunkSizeBytes = 64;
                options.SetBudget(_cpu, 4096, 8192);
            });
            services.AddResources("test", builder =>
            {
                builder.UseSource<FakeSource>().UseRuntimeDependency<FakeRuntime>("device");
                configure(builder);
            });
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _scope = _provider.CreateAsyncScope();
            Memory = _scope.ServiceProvider.GetRequiredService<IMemoryManager>();
            Manager = _scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("test");
        }

        internal BuildPlan Plan { get; } = new();

        internal SourceStore Source { get; } = new();

        internal RuntimeState Runtime { get; } = new();

        internal IMemoryManager Memory { get; }

        internal ResourceManager Manager { get; }

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class BuildPlan
    {
        internal Dictionary<string, Func<ResourceBuildContext, CancellationToken, ValueTask<TestValue>>> Builds { get; } = [];

        internal ConcurrentDictionary<string, int> BuildCounts { get; } = new();

        internal ConcurrentQueue<string> Released { get; } = new();

        internal Func<TestValue, ValueTask>? Release { get; set; }
    }

    private sealed class TestValue(string text, TestValue? dependency = null)
    {
        internal string Text { get; } = text;

        internal TestValue? Dependency { get; } = dependency;

        internal bool Released { get; set; }
    }

    private sealed class PlannedStage(BuildPlan plan) : IResourceSourceStage<TestValue>
    {
        public async ValueTask<TestValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            plan.BuildCounts.AddOrUpdate(context.PipelineId, 1, static (_, count) => count + 1);
            if (plan.Builds.TryGetValue(context.PipelineId, out Func<ResourceBuildContext, CancellationToken, ValueTask<TestValue>>? build))
            {
                return await build(context, cancellationToken);
            }

            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
            using StreamReader reader = new(stream);
            return new(await reader.ReadToEndAsync(cancellationToken));
        }

        public async ValueTask ReleaseAsync(TestValue resource)
        {
            if (plan.Release is not null)
            {
                await plan.Release(resource);
            }

            Assert.False(resource.Released);
            resource.Released = true;
            plan.Released.Enqueue(resource.Text);
        }
    }

    private sealed class DocumentValue(ResourceId id, string text) : IResourceDocument
    {
        public ResourceId Id { get; } = id;

        public IReadOnlyList<ResourceSubresource> Subresources { get; } =
        [
            new() { Selector = "mesh/0", Kind = "mesh", PipelineIds = ["mesh"] },
            new() { Selector = "image/1", Kind = "image", PipelineIds = ["image"] },
        ];

        internal string Text { get; } = text;
    }

    private sealed class DocumentStage(BuildPlan plan) : IResourceSourceStage<DocumentValue>
    {
        public async ValueTask<DocumentValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
            using StreamReader reader = new(stream);
            return new(context.Address.Id, await reader.ReadToEndAsync(cancellationToken));
        }

        public ValueTask ReleaseAsync(DocumentValue resource)
        {
            plan.Released.Enqueue("document");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CpuValue(string text, DocumentValue? document = null)
    {
        internal string Text { get; } = text;

        internal DocumentValue? Document { get; } = document;
    }

    private sealed class ImageValue(string text, DocumentValue document)
    {
        internal string Text { get; } = text;

        internal DocumentValue Document { get; } = document;
    }

    private sealed class CpuStage(BuildPlan plan) : IResourceTransform<DocumentValue, CpuValue>
    {
        public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

        public ResourceAddress GetInput(ResourceAddress output) => output;

        public ValueTask<CpuValue> BuildAsync(DocumentValue input, ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new CpuValue($"decoded:{input.Text}"));

        public ValueTask ReleaseAsync(CpuValue resource)
        {
            plan.Released.Enqueue("cpu");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UploadStage(BuildPlan plan) : IResourceTransform<CpuValue, TestValue>
    {
        public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

        public ResourceAddress GetInput(ResourceAddress output) => output;

        public ValueTask<TestValue> BuildAsync(CpuValue input, ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new TestValue($"uploaded:{input.Text}"));

        public ValueTask ReleaseAsync(TestValue resource)
        {
            resource.Released = true;
            plan.Released.Enqueue("gpu");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MeshProjection : IResourceTransform<DocumentValue, CpuValue>
    {
        public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

        public ResourceAddress GetInput(ResourceAddress output) => new(output.Id, variant: "document");

        public ValueTask<CpuValue> BuildAsync(DocumentValue input, ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new CpuValue($"{context.Address.Subresource}:{context.Address.Variant}", input));

        public ValueTask ReleaseAsync(CpuValue resource) => ValueTask.CompletedTask;
    }

    private sealed class ImageProjection : IResourceTransform<DocumentValue, ImageValue>
    {
        public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

        public ResourceAddress GetInput(ResourceAddress output) => new(output.Id, variant: "document");

        public ValueTask<ImageValue> BuildAsync(DocumentValue input, ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new ImageValue($"{context.Address.Subresource}:{context.Address.Variant}", input));

        public ValueTask ReleaseAsync(ImageValue resource) => ValueTask.CompletedTask;
    }

    private sealed class SourceStore
    {
        private int _revision;
        private int _opens;

        internal ConcurrentDictionary<ResourceId, SourceEntry> Entries { get; } = new();

        internal int OpenCount => Volatile.Read(ref _opens);

        internal void Set(string id, string text)
        {
            int revision = Interlocked.Increment(ref _revision);
            Entries[new(id)] = new(revision.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(text));
        }

        internal ResourceSourceSnapshot Open(ResourceId id)
        {
            Interlocked.Increment(ref _opens);
            if (!Entries.TryGetValue(id, out SourceEntry? entry))
            {
                throw new FileNotFoundException(id.Value);
            }

            return new FakeSnapshot(id, entry);
        }
    }

    private sealed record SourceEntry(string Revision, byte[] Bytes);

    private sealed class FakeSource(SourceStore store) : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(store.Open(id));
        }

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(store.Entries.TryGetValue(id, out SourceEntry? entry) ? entry.Revision : null);
        }
    }

    private sealed class FakeSnapshot(ResourceId id, SourceEntry entry) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => entry.Revision;

        public override long? Length => entry.Bytes.Length;

        public override bool CanReadRange => true;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = new MemoryStream(entry.Bytes, checked((int)offset), checked((int)(length ?? (entry.Bytes.Length - offset))), false);
            return ValueTask.FromResult(stream);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RuntimeState
    {
        public event EventHandler? Changed;

        internal ResourceRuntimeSnapshot Snapshot { get; private set; } = new(1, true, 0);

        internal void Set(ResourceRuntimeSnapshot snapshot)
        {
            Snapshot = snapshot;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeRuntime(RuntimeState state) : IResourceRuntimeDependency
    {
        public event EventHandler? Changed
        {
            add => state.Changed += value;
            remove => state.Changed -= value;
        }

        public string Id => "device";

        public ResourceRuntimeSnapshot Snapshot => state.Snapshot;
    }

    private sealed class ServiceCounts
    {
        internal int SourceInstances { get; set; }

        internal int StageInstances { get; set; }

        internal int SourceDisposals { get; set; }

        internal int StageDisposals { get; set; }
    }

    private sealed class CountingSource : IResourceSource, IDisposable
    {
        private readonly ServiceCounts _counts;

        public CountingSource(ServiceCounts counts)
        {
            _counts = counts;
            counts.SourceInstances++;
        }

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);

        public void Dispose() => _counts.SourceDisposals++;
    }

    private sealed class CountingStage(ServiceCounts counts) : IResourceSourceStage<TestValue>, IDisposable
    {
        private readonly string _id = (++counts.StageInstances).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public ValueTask<TestValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new TestValue(_id));

        public ValueTask ReleaseAsync(TestValue resource) => ValueTask.CompletedTask;

        public void Dispose() => counts.StageDisposals++;
    }
}
