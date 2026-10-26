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
            .AddSourcePipeline<DocumentValue, DocumentStage>()
            .AddTransformPipeline<DocumentValue, CpuValue, CpuStage>()
            .AddTransformPipeline<CpuValue, TestValue, UploadStage>());
        fixture.Source.Set("asset", "encoded");

        using ResourceLease<TestValue> first = await fixture.Manager.AcquireLeaseForTestAsync(Key("gpu"));
        using ResourceLease<TestValue> second = await fixture.Manager.AcquireLeaseForTestAsync(Key("gpu"));
        Assert.Same(first.Value, second.Value);
        Assert.Equal("uploaded:decoded:encoded", first.Value.Text);
        Assert.Equal(1, fixture.Source.OpenCount);
        Assert.Equal(2, await fixture.Manager.TrimAsync());
        Assert.Equal(["cpu", "document"], fixture.Plan.Released.Order());
        Assert.False(first.Value.Released);
    }

    /// <summary>Typed stages are passed directly without DI or pipeline wrappers and retain build dependency cleanup.</summary>
    /// <returns>Completion of manual construction, shared acquisition and release.</returns>
    [Fact]
    public async Task ManualConstructionAcceptsTypedStagesDirectlyAsync()
    {
        await using MemoryManager memory = new();
        await using ResourceExecutionHost execution = new();
        BuildPlan plan = new();
        SourceStore store = new();
        store.Set("asset", "initial");
        await using ResourceManager manager = new(
            "manual",
            new FakeSource(store),
            [new DocumentStage(plan), new CpuStage(plan), new UploadStage(plan)],
            memory,
            execution.Resources);
        using ResourceLease<TestValue> first = await manager.AcquireLeaseForTestAsync(Key("value"));
        using (ResourceLease<TestValue> second = await manager.AcquireLeaseForTestAsync(Key("value")))
        {
            Assert.Same(first.Value, second.Value);
            Assert.Equal("uploaded:decoded:initial", first.Value.Text);
            Assert.Equal(2, await manager.TrimAsync());
            Assert.Equal(["cpu", "document"], plan.Released.Order());
        }

        first.Dispose();
        Assert.Equal(1, await manager.TrimAsync());
        Assert.Equal(["cpu", "document", "gpu"], plan.Released.Order());
    }

    /// <summary>A resident diamond shares its leaf and releases every parent before the leaf.</summary>
    /// <returns>Completion of dependency sharing and release.</returns>
    [Fact]
    public async Task ResidentDiamondSharesLeafAndTrimsParentsFirstAsync()
    {
        await using Fixture fixture = new(builder => builder
            .AddSourcePipeline<TestValue, PlannedStage>());
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

        ResourceLease<TestValue> root = await fixture.Manager.AcquireLeaseForTestAsync(Key("root"));
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
        await using Fixture fixture = CreatePlannedFixture();
        fixture.Plan.Builds["leaf"] = (_, _) => ValueTask.FromResult(new TestValue("leaf"));
        fixture.Plan.Builds["root"] = (context, token) => context.RequireAsync(Key("leaf"), cancellationToken: token);
        ResourceLease<TestValue> leaf = await fixture.Manager.AcquireLeaseForTestAsync(Key("leaf"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.AcquireLeaseForTestAsync(Key("root")));
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
            .AddSourcePipeline<DocumentValue, DocumentStage>()
            .AddTransformPipeline<DocumentValue, CpuValue, MeshProjection>()
            .AddTransformPipeline<DocumentValue, ImageValue, ImageProjection>());
        fixture.Source.Set("asset", "document bytes");
        using ResourceLease<CpuValue> mesh = await fixture.Manager.AcquireLeaseForTestAsync(new ResourceKey<CpuValue>("asset", "mesh/0", "tangent"));
        using ResourceLease<CpuValue> other = await fixture.Manager.AcquireLeaseForTestAsync(new ResourceKey<CpuValue>("asset", "mesh/0", "position"));
        using ResourceLease<ImageValue> image = await fixture.Manager.AcquireLeaseForTestAsync(new ResourceKey<ImageValue>("asset", "image/1", "linear"));

        Assert.Equal("mesh/0:tangent", mesh.Value.Text);
        Assert.Equal("mesh/0:position", other.Value.Text);
        Assert.Equal("image/1:linear", image.Value.Text);
        Assert.NotSame(mesh.Value, other.Value);
        Assert.Same(mesh.Value.Document, other.Value.Document);
        Assert.Same(mesh.Value.Document, image.Value.Document);
        Assert.Equal(1, fixture.Source.OpenCount);
        Assert.Equal(new ResourceId("asset"), mesh.Value.Document!.Id);
        Assert.Equal("document bytes", mesh.Value.Document.Text);
        Assert.Equal(0, await fixture.Manager.TrimAsync());
    }

    /// <summary>One cancelled caller leaves a shared asynchronous load running for the remaining caller.</summary>
    /// <returns>Completion of both callers.</returns>
    [Fact]
    public async Task ConcurrentAcquireSharesBuildAndIsolatesCallerCancellationAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
        TaskCompletionSource entered = NewSignal();
        TaskCompletionSource resume = NewSignal();
        fixture.Plan.Builds["value"] = async (_, token) =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(token);
            return new("value");
        };
        using CancellationTokenSource cancellation = new();
        Task<ResourceLease<TestValue>> cancelled = fixture.Manager.AcquireLeaseForTestAsync(Key("value"), cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<ResourceLease<TestValue>> surviving = fixture.Manager.AcquireLeaseForTestAsync(Key("value")).AsTask();
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
        using ResourceLease<TestValue> cached = await fixture.Manager.AcquireLeaseForTestAsync(Key("value"));
        Assert.Same(lease.Value, cached.Value);
        Assert.Equal(1, fixture.Plan.BuildCounts["value"]);
    }

    /// <summary>Dependencies between separately started builds fail with a cycle instead of deadlocking.</summary>
    /// <returns>Completion of both failed builds.</returns>
    [Fact]
    public async Task IndependentBuildTasksDetectDynamicCycleAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
        TaskCompletionSource bothStarted = NewSignal();
        int started = 0;
        async ValueTask<TestValue> BuildAsync(ResourceBuildContext context, CancellationToken token)
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.SetResult();
            }

            await bothStarted.Task.WaitAsync(token);
            string dependency = context.Address.Subresource == "left" ? "right" : "left";
            return new(context.Address.Subresource, await context.RequireAsync(Key(dependency), cancellationToken: token));
        }

        fixture.Plan.Builds["left"] = BuildAsync;
        fixture.Plan.Builds["right"] = BuildAsync;
        Task<ResourceLease<TestValue>> left = fixture.Manager.AcquireLeaseForTestAsync(Key("left")).AsTask();
        Task<ResourceLease<TestValue>> right = fixture.Manager.AcquireLeaseForTestAsync(Key("right")).AsTask();
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
        await using Fixture fixture = CreatePlannedFixture();
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

        ResourceLease<TestValue> lease = await fixture.Manager.AcquireLeaseForTestAsync(Key("root"));
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
        await using Fixture fixture = CreatePlannedFixture();
        fixture.Plan.Builds["leaf"] = (_, _) => ValueTask.FromResult(new TestValue("leaf"));
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            TestValue build = await context.RequireAsync(Key("leaf"), ResourceDependencyLifetime.Build, token);
            TestValue resident = await context.RequireAsync(Key("leaf"), ResourceDependencyLifetime.Resident, token);
            Assert.Same(build, resident);
            return new("root", resident);
        };
        ResourceLease<TestValue> root = await fixture.Manager.AcquireLeaseForTestAsync(Key("root"));
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
        await using Fixture fixture = CreatePlannedFixture();
        bool cleanupBlocked = true;
        int completedCleanupCalls = 0;
        TaskCompletionSource retryStarted = NewSignal();
        TaskCompletionSource finishRetry = NewSignal();
        fixture.Plan.Builds["value"] = async (context, token) =>
        {
            await context.GetMemoryScope(ResourceDependencyLifetime.Build)
                .RentAsync<byte>(_cpu, 12, MemoryPurpose.Build, token);
            context.RegisterCleanup(async () =>
            {
                if (cleanupBlocked)
                {
                    throw new IOException("cleanup not yet safe");
                }

                retryStarted.TrySetResult();
                await finishRetry.Task;
            });
            context.RegisterCleanup(() =>
            {
                completedCleanupCalls++;
                return ValueTask.CompletedTask;
            });
            return new("value");
        };

        await Assert.ThrowsAsync<AggregateException>(async () => await fixture.Manager.AcquireLeaseForTestAsync(Key("value")));
        Assert.Equal(1, completedCleanupCalls);
        Assert.Equal(["value"], fixture.Plan.Released);
        Assert.True(fixture.Memory.GetSnapshot(_cpu).CommittedBytes > 0);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.AcquireLeaseForTestAsync(Key("value")));
        Assert.Equal(1, fixture.Plan.BuildCounts["value"]);

        cleanupBlocked = false;
        Task<int> trim = fixture.Manager.TrimAsync().AsTask();
        try
        {
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.AcquireLeaseForTestAsync(Key("value")));
            Assert.Equal(1, fixture.Plan.BuildCounts["value"]);
            Assert.True(fixture.Memory.GetSnapshot(_cpu).CommittedBytes > 0);
        }
        finally
        {
            finishRetry.TrySetResult();
            Assert.Equal(1, await trim.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        Assert.Equal(1, completedCleanupCalls);
        Assert.Equal(["value"], fixture.Plan.Released);
        Assert.Equal(0UL, fixture.Memory.GetSnapshot(_cpu).CommittedBytes);
        using ResourceLease<TestValue> retry = await fixture.Manager.AcquireLeaseForTestAsync(Key("value"));
        Assert.Equal(2, fixture.Plan.BuildCounts["value"]);
        Assert.False(retry.Value.Released);
    }

    /// <summary>A frame pins concrete values while references follow reload and can be returned independently.</summary>
    /// <returns>Completion of frame acquisition, reload and independent ownership checks.</returns>
    [Fact]
    public async Task ReferenceAcquiresStableFrameLeasesAcrossReloadAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
        fixture.Source.Set("asset", "old");
        using ResourceReference<TestValue> reference = await fixture.Manager.AcquireAsync(Key("value"));
        using ResourceSnapshot frame = fixture.Manager.CaptureSnapshot();
        using ResourceLease<TestValue> old = reference.Acquire();
        fixture.Source.Set("asset", "new");
        Assert.Equal(ResourceReloadStatus.Published, (await fixture.Manager.ReloadForTestAsync([new("asset")])).Status);
        using ResourceLease<TestValue> current = reference.Acquire();
        using ResourceLease<TestValue> captured = reference.Acquire(frame);
        Assert.Same(old.Value, captured.Value);
        Assert.Equal(old.Revision, captured.Revision);
        Assert.NotEqual(old.Revision, current.Revision);
        Assert.Equal("old", old.Value.Text);
        Assert.Equal("new", current.Value.Text);
        reference.Dispose();
        Assert.False(reference.IsAvailable);
        Assert.Throws<ObjectDisposedException>(() => reference.Acquire());
        Assert.Throws<ObjectDisposedException>(() => reference.TryAcquire(out _));
        Assert.Equal("old", old.Value.Text);
        Assert.Equal("new", current.Value.Text);
        Assert.Equal(0, await fixture.Manager.TrimAsync());
    }

    /// <summary>A handle resolves to an owning reference and its lease survives returning the last reference.</summary>
    /// <returns>Completion of handle acquisition and separate reference and lease ownership.</returns>
    [Fact]
    public async Task ManagerResolvesHandlesToOwningReferencesAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
        fixture.Source.Set("asset", "value");
        using ResourceReference<TestValue> first = await fixture.Manager.AcquireAsync(Key("value"));
        ResourceHandle<TestValue> handle = first.Handle;
        Assert.True(fixture.Manager.TryAcquire(handle, out ResourceReference<TestValue>? second));
        using (second)
        {
            first.Dispose();
            Assert.True(second!.IsAvailable);
            Assert.Equal(handle, second.Handle);
            using ResourceLease<TestValue> lease = second.Acquire();
            second.Dispose();
            Assert.False(fixture.Manager.TryAcquire(handle, out _));
            Assert.Equal("value", lease.Value.Text);
            Assert.Equal(0, await fixture.Manager.TrimAsync());
        }

        Assert.Equal(1, await fixture.Manager.TrimAsync());
    }

    /// <summary>A captured frame cannot resolve a resource loaded after that frame was opened.</summary>
    /// <returns>Completion of failed captured acquisition without changing the current reference.</returns>
    [Fact]
    public async Task ReferenceRejectsValuesMissingFromCapturedFrameAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
        fixture.Source.Set("asset", "value");
        using ResourceSnapshot frame = fixture.Manager.CaptureSnapshot();
        using ResourceReference<TestValue> reference = await fixture.Manager.AcquireAsync(Key("value"));
        Assert.False(reference.TryAcquire(out ResourceLease<TestValue>? missing, frame));
        Assert.Null(missing);
        Assert.Throws<InvalidOperationException>(() => reference.Acquire(frame));
        using ResourceLease<TestValue> current = reference.Acquire();
        Assert.Equal("value", current.Value.Text);
        frame.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reference.Acquire(frame));
    }

    /// <summary>A handle is unmanaged and non-owning, and an expired generation cannot resolve after reuse.</summary>
    /// <returns>Completion of the handle ownership checks.</returns>
    [Fact]
    public async Task HandlesAreUnmanagedAndRejectExpiredOrForeignOwnersAsync()
    {
        Assert.Equal(16, UnmanagedSize<ResourceHandle<TestValue>>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ResourceHandle<TestValue>>());
        await using Fixture fixture = CreatePlannedFixture();
        await using Fixture other = CreatePlannedFixture();
        fixture.Plan.Builds["value"] = (_, _) => ValueTask.FromResult(new TestValue("value"));
        using ResourceReference<TestValue> owner = await fixture.Manager.AcquireAsync(Key("value"));
        ResourceHandle<TestValue> handle = owner.Handle;
        ResourceHandle<TestValue> copy = handle;
        ResourceHandle<CpuValue> wrongType = Unsafe.As<ResourceHandle<TestValue>, ResourceHandle<CpuValue>>(ref copy);
        using ResourceSnapshot captured = fixture.Manager.CaptureSnapshot();
        Assert.False(handle.IsDefault);
        Assert.False(other.Manager.TryAcquireLeaseForTest(copy, out ResourceLease<TestValue>? foreign));
        Assert.Null(foreign);
        Assert.False(fixture.Manager.TryAcquireLeaseForTest(default(ResourceHandle<TestValue>), out ResourceLease<TestValue>? empty));
        Assert.Null(empty);
        Assert.False(fixture.Manager.TryAcquireLeaseForTest(wrongType, out ResourceLease<CpuValue>? mismatched));
        Assert.Null(mismatched);
        Assert.True(fixture.Manager.TryAcquireLeaseForTest(handle, out ResourceLease<TestValue>? lease));
        using (lease)
        {
            owner.Dispose();
            Assert.False(fixture.Manager.TryAcquireLeaseForTest(copy, out ResourceLease<TestValue>? expired));
            Assert.Null(expired);
            Assert.False(captured.TryAcquire(copy, out expired));
            Assert.Null(expired);
            Assert.Equal("value", lease!.Value.Text);
            using ResourceReference<TestValue> replacement = await fixture.Manager.AcquireAsync(Key("value"));
            Assert.NotEqual(handle, replacement.Handle);
            Assert.False(fixture.Manager.TryAcquireLeaseForTest(handle, out expired));
            Assert.True(fixture.Manager.TryAcquireLeaseForTest(replacement.Handle, out ResourceLease<TestValue>? current));
            current!.Dispose();
        }
    }

    /// <summary>Reload publishes all affected outputs together and captured resource snapshots keep the earlier map.</summary>
    /// <returns>Completion of atomic reload and old-generation ownership checks.</returns>
    [Fact]
    public async Task ReloadPublishesAtomicallyAndKeepsOldLeasesAndSnapshotsAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
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
        using ResourceReference<TestValue> root = await fixture.Manager.AcquireAsync(Key("root"));
        using ResourceReference<TestValue> leaf = await fixture.Manager.AcquireAsync(Key("leaf"));
        using ResourceSnapshot before = fixture.Manager.CaptureSnapshot();
        using ResourceLease<TestValue> oldLease = await fixture.Manager.AcquireLeaseForTestAsync(Key("root"));
        ResourceHandle<TestValue> handle = root.Handle;
        ulong version = fixture.Manager.PublicationVersion;
        fixture.Source.Set("asset", "new");
        Task<ResourceReloadResult> reload = fixture.Manager.ReloadForTestAsync([new("asset")]).AsTask();
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

    /// <summary>A changed image rebuilds its parent while another scene and their shared geometry are reused.</summary>
    /// <returns>Completion of selective reload and unchanged notification checks.</returns>
    [Fact]
    public async Task ReloadRebuildsOnlyChangedInputAndItsDependentsAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
        var imageA = new ResourceKey<TestValue>("a.png", "image-a");
        var imageB = new ResourceKey<TestValue>("b.png", "image-b");
        var geometry = new ResourceKey<TestValue>("geometry.bin", "geometry");
        fixture.Source.Set("a.png", "a-old");
        fixture.Source.Set("b.png", "b");
        fixture.Source.Set("geometry.bin", "geometry");
        fixture.Plan.Builds["root-a"] = async (context, token) =>
        {
            await context.RequireAsync(geometry, cancellationToken: token);
            TestValue image = await context.RequireAsync(imageA, cancellationToken: token);
            return new(image.Text, image);
        };
        fixture.Plan.Builds["root-b"] = async (context, token) =>
        {
            await context.RequireAsync(geometry, cancellationToken: token);
            TestValue image = await context.RequireAsync(imageB, cancellationToken: token);
            return new(image.Text, image);
        };
        using ResourceReference<TestValue> ownerA = await fixture.Manager.AcquireAsync(Key("root-a"));
        using ResourceReference<TestValue> ownerB = await fixture.Manager.AcquireAsync(Key("root-b"));
        using ResourceLease<TestValue> previousA = await fixture.Manager.AcquireLeaseForTestAsync(Key("root-a"));
        using ResourceLease<TestValue> previousB = await fixture.Manager.AcquireLeaseForTestAsync(Key("root-b"));
        using ResourceLease<TestValue> previousGeometry = await fixture.Manager.AcquireLeaseForTestAsync(geometry);
        Assert.Equal(3, fixture.Source.OpenCount);
        fixture.Source.Set("a.png", "a-new");

        ResourceReloadResult changed = await fixture.Manager.ReloadForTestAsync([imageA.Address.Id]);
        Assert.Equal(ResourceReloadStatus.Published, changed.Status);
        Assert.Equal(2, changed.UpdatedResources);
        using ResourceLease<TestValue> currentA = await fixture.Manager.AcquireLeaseForTestAsync(Key("root-a"));
        using ResourceLease<TestValue> currentB = await fixture.Manager.AcquireLeaseForTestAsync(Key("root-b"));
        using ResourceLease<TestValue> currentGeometry = await fixture.Manager.AcquireLeaseForTestAsync(geometry);
        Assert.Equal("a-new", currentA.Value.Text);
        Assert.Equal("a-old", previousA.Value.Text);
        Assert.Same(previousB.Value, currentB.Value);
        Assert.Equal(previousB.Revision, currentB.Revision);
        Assert.Same(previousGeometry.Value, currentGeometry.Value);
        Assert.Equal(previousGeometry.Revision, currentGeometry.Revision);
        Assert.Equal(2, fixture.Plan.BuildCounts["image-a"]);
        Assert.Equal(2, fixture.Plan.BuildCounts["root-a"]);
        Assert.Equal(1, fixture.Plan.BuildCounts["image-b"]);
        Assert.Equal(1, fixture.Plan.BuildCounts["root-b"]);
        Assert.Equal(1, fixture.Plan.BuildCounts["geometry"]);
        Assert.Equal(4, fixture.Source.OpenCount);

        ResourceReloadResult unchanged = await fixture.Manager.ReloadForTestAsync([imageA.Address.Id, imageA.Address.Id, new("unknown")]);
        Assert.Equal(ResourceReloadStatus.Unchanged, unchanged.Status);
        Assert.Equal(0, unchanged.UpdatedResources);
        Assert.Equal(changed.PublicationVersion, unchanged.PublicationVersion);
        Assert.Equal(4, fixture.Source.OpenCount);
        Assert.Equal(2, fixture.Plan.BuildCounts["image-a"]);
        Assert.Equal(2, fixture.Plan.BuildCounts["root-a"]);
    }

    /// <summary>Failure in an affected parent preserves the previously published graph and version.</summary>
    /// <returns>Completion of the rejected reload.</returns>
    [Fact]
    public async Task FailedReloadPreservesPublishedGraphAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
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
        using ResourceReference<TestValue> root = await fixture.Manager.AcquireAsync(Key("root"));
        using ResourceReference<TestValue> leaf = await fixture.Manager.AcquireAsync(Key("leaf"));
        ulong version = fixture.Manager.PublicationVersion;
        fixture.Source.Set("asset", "broken");

        await Assert.ThrowsAsync<IOException>(async () => await fixture.Manager.ReloadForTestAsync([new("asset")]));
        Assert.Equal(version, fixture.Manager.PublicationVersion);
        AssertHandleText(fixture.Manager, root.Handle, "old");
        AssertHandleText(fixture.Manager, leaf.Handle, "old");
    }

    /// <summary>A source change while a candidate is built rejects publication and retains the old graph.</summary>
    /// <returns>Completion of the conflicted reload.</returns>
    [Fact]
    public async Task ReloadRejectsSourceRevisionChangedDuringConstructionAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
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
        using ResourceReference<TestValue> owner = await fixture.Manager.AcquireAsync(Key("root"));
        ulong version = fixture.Manager.PublicationVersion;
        fixture.Source.Set("asset", "candidate");
        Task<ResourceReloadResult> reload = fixture.Manager.ReloadForTestAsync([new("asset")]).AsTask();
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
        await using Fixture fixture = CreatePlannedFixture();
        fixture.Source.Set("asset", "old");
        fixture.Plan.Builds["root"] = async (context, token) =>
        {
            TestValue input = await context.RequireAsync(Key("leaf"), ResourceDependencyLifetime.Build, token);
            return new(input.Text);
        };
        using ResourceReference<TestValue> owner = await fixture.Manager.AcquireAsync(Key("root"));
        Assert.Equal(1, await fixture.Manager.TrimAsync());
        Assert.Equal(1, fixture.Plan.BuildCounts["leaf"]);
        fixture.Source.Set("asset", "new");

        ResourceReloadResult result = rescan
            ? await fixture.Manager.ReloadAllForTestAsync()
            : await fixture.Manager.ReloadForTestAsync([new("asset")]);
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
        await using Fixture fixture = CreatePlannedFixture();
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
        using ResourceReference<TestValue> gpu = await fixture.Manager.AcquireAsync(Key("gpu"));
        using ResourceReference<TestValue> scene = await fixture.Manager.AcquireAsync(Key("scene"));
        using ResourceReference<TestValue> cpu = await fixture.Manager.AcquireAsync(Key("cpu"));
        using ResourceLease<TestValue> oldGpu = await fixture.Manager.AcquireLeaseForTestAsync(Key("gpu"));
        using ResourceSnapshot oldFrame = fixture.Manager.CaptureSnapshot();
        ResourceHandle<TestValue> handle = gpu.Handle;
        fixture.Runtime.Set(new(2, false, 1));

        Assert.False(gpu.IsAvailable);
        Assert.False(scene.IsAvailable);
        Assert.False(oldGpu.IsUsable);
        Assert.Throws<ResourceUnavailableException>(() => oldGpu.Value);
        Assert.False(fixture.Manager.TryAcquireLeaseForTest(handle, out ResourceLease<TestValue>? invalid));
        Assert.Null(invalid);
        Assert.False(oldFrame.TryAcquire(handle, out invalid));
        AssertHandleText(fixture.Manager, cpu.Handle, "cpu");
        await Assert.ThrowsAsync<ResourceUnavailableException>(async () => await fixture.Manager.AcquireLeaseForTestAsync(Key("gpu")));

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
    public async Task ShutdownRejectsReferencesLeasesAndSnapshotsAsync()
    {
        await using Fixture fixture = CreatePlannedFixture();
        fixture.Plan.Builds["value"] = (_, _) => ValueTask.FromResult(new TestValue("value"));
        ResourceReference<TestValue> owner = await fixture.Manager.AcquireAsync(Key("value"));
        ResourceLease<TestValue> lease = await fixture.Manager.AcquireLeaseForTestAsync(Key("value"));
        ResourceSnapshot read = fixture.Manager.CaptureSnapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.DisposeAsync());
        owner.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.DisposeAsync());
        lease.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.DisposeAsync());
        read.Dispose();
        await fixture.Manager.DisposeAsync();
        await fixture.Manager.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await fixture.Manager.AcquireLeaseForTestAsync(Key("value")));
    }

    /// <summary>All scopes share one manager, its source and pipelines until the application root is disposed.</summary>
    /// <returns>Completion of the shared manager and its application services.</returns>
    [Fact]
    public async Task DependencyInjectionSharesManagerAcrossScopesAndOwnsServicesOnceAsync()
    {
        ServiceCollection services = new();
        ServiceCounts counts = new();
        services.AddSingleton(counts);
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<CountingSource>()
            .AddSourcePipeline<TestValue, CountingStage<TestValue>>()
            .AddSourcePipeline<CpuValue, CountingStage<CpuValue>>());

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using ResourceExecutionHost execution = new(provider);
        await using AsyncServiceScope firstScope = provider.CreateAsyncScope();
        await using AsyncServiceScope secondScope = provider.CreateAsyncScope();
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        Assert.Same(manager, firstScope.ServiceProvider.GetRequiredService<ResourceManager>());
        Assert.Same(manager, secondScope.ServiceProvider.GetRequiredService<ResourceManager>());
        using (ResourceLease<TestValue> one = await manager.AcquireLeaseForTestAsync(Key("one")))
        using (ResourceLease<CpuValue> two = await manager.AcquireLeaseForTestAsync(new ResourceKey<CpuValue>("asset", "one")))
        using (ResourceLease<TestValue> shared = await secondScope.ServiceProvider.GetRequiredService<ResourceManager>().AcquireLeaseForTestAsync(Key("one")))
        {
            Assert.NotEqual(one.Value.Text, two.Value.Text);
            Assert.Same(one.Value, shared.Value);
        }

        await firstScope.DisposeAsync();
        await secondScope.DisposeAsync();
        Assert.Equal(0, counts.SourceDisposals);
        Assert.Equal(0, counts.StageDisposals);
        using (ResourceLease<TestValue> retained = await manager.AcquireLeaseForTestAsync(Key("one")))
        {
            Assert.False(retained.Value.Released);
        }

        await manager.DisposeAsync();
        Assert.Equal(0, counts.SourceDisposals);
        Assert.Equal(0, counts.StageDisposals);
        Assert.Equal(1, counts.SourceInstances);
        Assert.Equal(2, counts.StageInstances);
        await execution.DisposeAsync();
        await provider.DisposeAsync();
        Assert.Equal(1, counts.SourceDisposals);
        Assert.Equal(2, counts.StageDisposals);
    }

    /// <summary>Failed manager cleanup retains singleton services until a successful retry and root disposal.</summary>
    /// <returns>Completion of cleanup retry and application shutdown.</returns>
    [Fact]
    public async Task DependencyInjectionRetainsServicesUntilCleanupRetryCompletesAsync()
    {
        ServiceCollection services = new();
        ServiceCounts counts = new() { FailReleaseOnce = true };
        services.AddSingleton(counts);
        services.AddMemoryManagement();
        services.AddResources(builder => builder.UseSource<CountingSource>()
            .AddSourcePipeline<TestValue, CountingStage<TestValue>>());
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using ResourceExecutionHost execution = new(provider);
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        using (ResourceLease<TestValue> lease = await manager.AcquireLeaseForTestAsync(Key("retry")))
        {
            Assert.NotNull(lease.Value);
        }

        await Assert.ThrowsAsync<AggregateException>(async () => await manager.DisposeAsync());
        Assert.Same(manager, provider.GetRequiredService<ResourceManager>());
        Assert.Equal(0, counts.SourceDisposals);
        Assert.Equal(0, counts.StageDisposals);
        Assert.Equal(1, counts.ReleaseAttempts);
        await manager.DisposeAsync();
        Assert.Equal(2, counts.ReleaseAttempts);
        Assert.Equal(0, counts.StageDisposals);
        await execution.DisposeAsync();
        await provider.DisposeAsync();
        Assert.Equal(1, counts.SourceDisposals);
        Assert.Equal(1, counts.StageDisposals);
    }

    /// <summary>Duplicate registrations and mutation after configuration fail before manager use.</summary>
    [Fact]
    public void DependencyInjectionRejectsDuplicateAndLateConfiguration()
    {
        ServiceCollection services = new();
        ResourceRegistrationBuilder? captured = null;
        services.AddResources(builder =>
        {
            captured = builder;
            builder.UseSource<CountingSource>().AddSourcePipeline<TestValue, CountingStage<TestValue>>();
            Assert.Throws<InvalidOperationException>(() => builder.UseSource<CountingSource>());
            Assert.Throws<ArgumentException>(() => builder.AddSourcePipeline<TestValue, CountingStage<TestValue>>());
        });
        Assert.Throws<ArgumentException>(() => services.AddResources(_ => { }));
        Assert.Throws<InvalidOperationException>(() => captured!.AddSourcePipeline<TestValue, CountingStage<TestValue>>());
    }

    /// <summary>A pre-existing scoped source cannot be captured by the singleton manager.</summary>
    /// <returns>Completion of the rejected provider configuration.</returns>
    [Fact]
    public async Task DependencyInjectionRejectsScopedSourceAsync()
    {
        ServiceCollection services = new();
        services.AddSingleton(new ServiceCounts());
        services.AddMemoryManagement();
        services.AddScoped<CountingSource>();
        services.AddResources(builder => builder.UseSource<CountingSource>()
            .AddSourcePipeline<TestValue, CountingStage<TestValue>>());
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ResourceManager>());
    }

    /// <summary>Manager resolution requires the exact static input type; an unrelated output type cannot satisfy it.</summary>
    /// <param name="missing">Whether there is no other producer instead of a producer for an unrelated type.</param>
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
        services.AddResources(builder =>
        {
            builder.UseSource<FakeSource>().AddTransformPipeline<DocumentValue, CpuValue, CpuStage>();
            if (!missing)
            {
                builder.AddSourcePipeline<TestValue, PlannedStage>();
            }
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.ThrowsAny<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<ResourceManager>());
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
        services.AddResources(builder => builder.UseSource<FakeSource>()
            .AddSourcePipeline<TestValue, PlannedStage>().UseRuntimeDependency<FakeRuntime>("other"));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ResourceManager>());
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

    private static Fixture CreatePlannedFixture() => new(builder => builder.AddSourcePipeline<TestValue, PlannedStage>());

    private static ResourceKey<TestValue> Key(string selector) => new("asset", selector);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int UnmanagedSize<T>()
        where T : unmanaged => Unsafe.SizeOf<T>();

    private static void AssertHandleText(ResourceManager manager, ResourceHandle<TestValue> handle, string expected)
    {
        Assert.True(manager.TryAcquireLeaseForTest(handle, out ResourceLease<TestValue>? lease));
        using (lease)
        {
            Assert.Equal(expected, lease!.Value.Text);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ResourceExecutionHost _execution;
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
            services.AddResources(builder =>
            {
                builder.UseSource<FakeSource>().UseRuntimeDependency<FakeRuntime>("device");
                configure(builder);
            });
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _execution = new ResourceExecutionHost(_provider);
            _scope = _provider.CreateAsyncScope();
            Memory = _scope.ServiceProvider.GetRequiredService<IMemoryManager>();
            Manager = _scope.ServiceProvider.GetRequiredService<ResourceManager>();
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
            await _execution.DisposeAsync();
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
            plan.BuildCounts.AddOrUpdate(context.Address.Subresource, 1, static (_, count) => count + 1);
            if (plan.Builds.TryGetValue(context.Address.Subresource, out Func<ResourceBuildContext, CancellationToken, ValueTask<TestValue>>? build))
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

    private sealed class DocumentValue(ResourceId id, string text)
    {
        public ResourceId Id { get; } = id;

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

        internal bool FailReleaseOnce { get; set; }

        internal int ReleaseAttempts { get; set; }
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

    private sealed class CountingStage<T>(ServiceCounts counts) : IResourceSourceStage<T>, IDisposable
        where T : class
    {
        private readonly string _id = (++counts.StageInstances).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public ValueTask<T> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            object value = typeof(T) == typeof(TestValue) ? new TestValue(_id) : new CpuValue(_id);
            return ValueTask.FromResult((T)value);
        }

        public ValueTask ReleaseAsync(T resource)
        {
            counts.ReleaseAttempts++;
            if (counts.FailReleaseOnce)
            {
                counts.FailReleaseOnce = false;
                throw new InvalidOperationException("Retry output cleanup.");
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose() => counts.StageDisposals++;
    }
}
