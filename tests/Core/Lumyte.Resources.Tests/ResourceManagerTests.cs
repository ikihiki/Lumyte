using System.Collections.Concurrent;
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

        using ResourceLease<TestValue> first = await fixture.Manager.AcquireAsync(Key("gpu"));
        using ResourceLease<TestValue> second = await fixture.Manager.AcquireAsync(Key("gpu"));
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
        BuildPlan plan = new();
        SourceStore store = new();
        store.Set("asset", "initial");
        await using ResourceManager manager = new(
            "manual",
            new FakeSource(store),
            [new DocumentStage(plan), new CpuStage(plan), new UploadStage(plan)],
            memory);
        using ResourceLease<TestValue> first = await manager.AcquireAsync(Key("value"));
        using (ResourceLease<TestValue> second = await manager.AcquireAsync(Key("value")))
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
        await using Fixture fixture = CreatePlannedFixture();
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
            .AddSourcePipeline<DocumentValue, DocumentStage>()
            .AddTransformPipeline<DocumentValue, CpuValue, MeshProjection>()
            .AddTransformPipeline<DocumentValue, ImageValue, ImageProjection>());
        fixture.Source.Set("asset", "document bytes");
        using ResourceLease<CpuValue> mesh = await fixture.Manager.AcquireAsync(new ResourceKey<CpuValue>("asset", "mesh/0", "tangent"));
        using ResourceLease<CpuValue> other = await fixture.Manager.AcquireAsync(new ResourceKey<CpuValue>("asset", "mesh/0", "position"));
        using ResourceLease<ImageValue> image = await fixture.Manager.AcquireAsync(new ResourceKey<ImageValue>("asset", "image/1", "linear"));

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
        await using Fixture fixture = CreatePlannedFixture();
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

        await Assert.ThrowsAsync<AggregateException>(async () => await fixture.Manager.AcquireAsync(Key("value")));
        Assert.Equal(1, completedCleanupCalls);
        Assert.Equal(["value"], fixture.Plan.Released);
        Assert.True(fixture.Memory.GetSnapshot(_cpu).CommittedBytes > 0);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.AcquireAsync(Key("value")));
        Assert.Equal(1, fixture.Plan.BuildCounts["value"]);

        cleanupBlocked = false;
        Task<int> trim = fixture.Manager.TrimAsync().AsTask();
        try
        {
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Manager.AcquireAsync(Key("value")));
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
        using ResourceLease<TestValue> retry = await fixture.Manager.AcquireAsync(Key("value"));
        Assert.Equal(2, fixture.Plan.BuildCounts["value"]);
        Assert.False(retry.Value.Released);
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
                .AddSourcePipeline<TestValue, CountingStage<TestValue>>()
                .AddSourcePipeline<CpuValue, CountingStage<CpuValue>>());
        }

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager first = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("first");
        ResourceManager second = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("second");
        Assert.NotSame(first, second);
        Assert.Same(first, scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("first"));
        using (ResourceLease<TestValue> one = await first.AcquireAsync(Key("one")))
        using (ResourceLease<CpuValue> two = await first.AcquireAsync(new ResourceKey<CpuValue>("asset", "one")))
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
            builder.UseSource<CountingSource>().AddSourcePipeline<TestValue, CountingStage<TestValue>>();
            Assert.Throws<InvalidOperationException>(() => builder.UseSource<CountingSource>());
            Assert.Throws<ArgumentException>(() => builder.AddSourcePipeline<TestValue, CountingStage<TestValue>>());
        });
        Assert.Throws<ArgumentException>(() => services.AddResources("main", _ => { }));
        Assert.Throws<InvalidOperationException>(() => captured!.AddSourcePipeline<TestValue, CountingStage<TestValue>>());
        Assert.Throws<NotSupportedException>(() => new ServiceCollection().AddResources("singleton", builder =>
            builder.UseSource<CountingSource>(ServiceLifetime.Singleton)));
        Assert.Throws<NotSupportedException>(() => new ServiceCollection().AddResources("transient", builder =>
            builder.UseSource<CountingSource>().AddSourcePipeline<TestValue, CountingStage<TestValue>>(ServiceLifetime.Transient)));
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
        services.AddResources("invalid", builder =>
        {
            builder.UseSource<FakeSource>().AddTransformPipeline<DocumentValue, CpuValue, CpuStage>();
            if (!missing)
            {
                builder.AddSourcePipeline<TestValue, PlannedStage>();
            }
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.ThrowsAny<ArgumentException>(() => scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("invalid"));
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

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        internal Fixture(Action<ResourceRegistrationBuilder> configure)
        {
            ServiceCollection services = new();
            services.AddSingleton(Plan);
            services.AddSingleton(Source);
            services.AddMemoryManagement(options =>
            {
                options.ChunkSizeBytes = 64;
                options.SetBudget(_cpu, 4096, 8192);
            });
            services.AddResources("test", builder =>
            {
                builder.UseSource<FakeSource>();
                configure(builder);
            });
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _scope = _provider.CreateAsyncScope();
            Memory = _scope.ServiceProvider.GetRequiredService<IMemoryManager>();
            Manager = _scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("test");
        }

        internal BuildPlan Plan { get; } = new();

        internal SourceStore Source { get; } = new();

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

    private sealed class CountingStage<T>(ServiceCounts counts) : IResourceSourceStage<T>, IDisposable
        where T : class
    {
        private readonly string _id = (++counts.StageInstances).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public ValueTask<T> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            object value = typeof(T) == typeof(TestValue) ? new TestValue(_id) : new CpuValue(_id);
            return ValueTask.FromResult((T)value);
        }

        public ValueTask ReleaseAsync(T resource) => ValueTask.CompletedTask;

        public void Dispose() => counts.StageDisposals++;
    }
}
