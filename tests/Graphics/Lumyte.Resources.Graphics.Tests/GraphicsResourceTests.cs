using Lumyte.Graphics.Hosting;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Verifies resource generations against the real hosting lease and DI contracts.</summary>
public sealed class GraphicsResourceTests
{
    private static readonly ResourceKey<CpuValue> _cpuKey = new("models/test");
    private static readonly ResourceKey<GpuValue> _gpuKey = new("models/test");
    private static readonly ResourceKey<ReadbackValue> _readbackKey = new("models/test");

    /// <summary>Recovery rebuilds the GPU generation while preserving its cached CPU input.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task LossInvalidatesGpuAndRecoveryReusesCpuAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        GraphicsResourceRuntime runtime = provider.GetRequiredService<GraphicsResourceRuntime>();
        string runtimeId = runtime.Id;
        await devices.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        using ResourceLease<CpuValue> cpu = await resources.AcquireLeaseForTestAsync(_cpuKey);
        using ResourceReference<GpuValue> reference = await resources.AcquireAsync(_gpuKey);
        using ResourceLease<GpuValue> old = await resources.AcquireLeaseForTestAsync(_gpuKey);
        GpuValue oldValue = old.Value;
        Assert.Same(provider.GetRequiredService<GraphicsResourceRuntime>(), state.GpuRuntime);
        state.Devices[0].Lose();
        Assert.False(old.IsUsable);
        Assert.Throws<ResourceUnavailableException>(() => old.Value);
        Assert.False(resources.TryAcquireLeaseForTest(reference.Handle, out _));
        Assert.True(cpu.IsUsable);
        await devices.RecoverAsync();
        Assert.Equal(runtimeId, runtime.Id);
        ResourceReloadResult reload = await resources.RebuildRuntimeAsync([runtime.Id]);
        Assert.Equal(ResourceReloadStatus.Published, reload.Status);
        using ResourceLease<GpuValue> current = await resources.AcquireLeaseForTestAsync(_gpuKey);
        Assert.Same(state.Devices[1], current.Value.Device);
        Assert.True(cpu.IsUsable);
        Assert.Equal(1, state.CpuBuilds);
        Assert.True(current.Value.Lease.State.Epoch > oldValue.Lease.State.Epoch);
        Assert.False(oldValue.Released);
        Assert.Equal(0, await devices.RetireAsync());
        old.Dispose();
        await resources.TrimAsync();
        Assert.True(oldValue.Released);
        Assert.Same(provider.GetRequiredService<GraphicsResourceRuntime>(), state.GpuReleaseRuntime);
        Assert.Equal(1, await devices.RetireAsync());
        Assert.Equal(1, state.Devices[0].Disposals);
    }

    /// <summary>A device used only while building does not invalidate independent CPU results.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task BuildOnlyGraphicsDependencyDoesNotRetainTheDeviceAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        using ResourceLease<ReadbackValue> value = await resources.AcquireLeaseForTestAsync(_readbackKey);
        ReadbackValue original = value.Value;
        state.Devices[0].Lose();
        Assert.True(value.IsUsable);
        Assert.Same(original, value.Value);
        Assert.Equal(1, await devices.RetireAsync());
    }

    /// <summary>Different stages select independent named device environments while CPU work uses the shared default.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task StageBindingsSelectNamedGraphicsEnvironmentsAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state, true);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService main = provider.GetRequiredService<IGraphicsDeviceService>();
        IGraphicsDeviceService readback = provider.GetRequiredKeyedService<IGraphicsDeviceService>("readback");
        await main.InitializeAsync();
        await readback.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        using ResourceLease<GpuValue> gpu = await resources.AcquireLeaseForTestAsync(_gpuKey);
        using ResourceLease<ReadbackValue> cpu = await resources.AcquireLeaseForTestAsync(_readbackKey);
        Assert.Same(main, state.GpuRuntime!.Identity);
        Assert.Same(readback, state.ReadbackRuntime!.Identity);
        Assert.Same(provider.GetRequiredService<GraphicsResourceRuntime>(), state.GpuRuntime);
        Assert.Same(provider.GetResourceGraphicsRuntime("readback"), state.ReadbackRuntime);
        Assert.NotEqual(state.GpuRuntime.Id, state.ReadbackRuntime.Id);
        Assert.Same(provider.GetRequiredService<IResourceRuntime>(), state.CpuRuntime);

        state.Devices[0].Lose();
        Assert.False(gpu.IsUsable);
        Assert.True(cpu.IsUsable);
    }

    /// <summary>An explicitly named default device cannot replace or invalidate the unnamed environment.</summary>
    /// <param name="namedFirst">Whether the named resource environment is registered first.</param>
    /// <returns>Completion of the scenario.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnnamedAndNamedDefaultEnvironmentsAreIndependentAsync(bool namedFirst)
    {
        var state = new GraphicsTestState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        services.AddGraphicsDevice<TestFactory>("default", ServiceLifetime.Singleton);
        services.AddResources(builder => builder
            .UseSource<TestSource>()
            .AddSourcePipeline<CpuValue, CpuStage>()
            .AddTransformPipeline<CpuValue, GpuValue, GpuStage>()
            .UseStageRuntime<GpuValue, GraphicsResourceRuntime>());
        if (namedFirst)
        {
            services.AddResourceGraphics("default");
        }

        services.AddResourceGraphics();
        if (!namedFirst)
        {
            services.AddResourceGraphics("default");
        }

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService unnamed = provider.GetRequiredService<IGraphicsDeviceService>();
        IGraphicsDeviceService named = provider.GetRequiredKeyedService<IGraphicsDeviceService>("default");
        GraphicsResourceRuntime unnamedRuntime = provider.GetRequiredService<GraphicsResourceRuntime>();
        GraphicsResourceRuntime namedRuntime = provider.GetResourceGraphicsRuntime("default");
        Assert.Same(unnamed, unnamedRuntime.Identity);
        Assert.Same(named, namedRuntime.Identity);
        Assert.NotEqual(unnamedRuntime.Id, namedRuntime.Id);
        await unnamed.InitializeAsync();
        await named.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        using ResourceLease<GpuValue> gpu = await resources.AcquireLeaseForTestAsync(_gpuKey);
        Assert.Same(unnamedRuntime, state.GpuRuntime);

        state.Devices[1].Lose();
        Assert.True(gpu.IsUsable);
        state.Devices[0].Lose();
        Assert.False(gpu.IsUsable);
    }

    /// <summary>A device lost while a stage runs prevents publication and preserves cleanup ownership.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task LossDuringBuildRejectsTheCandidateAsync()
    {
        var state = new GraphicsTestState
        {
            BuildStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            BuildWait = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        Task<ResourceLease<GpuValue>> build = resources.AcquireLeaseForTestAsync(_gpuKey).AsTask();
        await state.BuildStarted.Task;
        state.Devices[0].Lose();
        state.BuildWait.SetResult();
        Exception? error = await Record.ExceptionAsync(async () =>
        {
            using ResourceLease<GpuValue> unexpected = await build;
        });
        Assert.NotNull(error);
        await resources.TrimAsync();
        Assert.True(Assert.Single(state.Values).Released);
        Assert.Equal(1, await devices.RetireAsync());
    }

    /// <summary>Failed retirement or native release retains the old epoch lease until a subsequent trim succeeds.</summary>
    /// <param name="failRetirement">Whether failure occurs during retirement confirmation instead of child release.</param>
    /// <returns>Completion of the scenario.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReleaseKeepsTheOwnerAvailableForRetryAsync(bool failRetirement)
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        using ResourceLease<GpuValue> lease = await resources.AcquireLeaseForTestAsync(_gpuKey);
        GpuValue value = lease.Value;
        state.Devices[0].Lose();
        lease.Dispose();
        state.ReleaseFailures = failRetirement ? 0 : 1;
        state.Devices[0].RejectRetirement = failRetirement;
        await Assert.ThrowsAsync<AggregateException>(() => resources.TrimAsync().AsTask());
        Assert.False(value.Released);
        Assert.Equal(0, await devices.RetireAsync());
        state.Devices[0].RejectRetirement = false;
        await resources.TrimAsync();
        Assert.True(value.Released);
        Assert.Equal(1, await devices.RetireAsync());
    }

    /// <summary>Matching names and versions from another DI root cannot identify the registered device.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task ForeignServiceWithTheSameVersionIsRejectedWithoutLeakingItsLeaseAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await using ServiceProvider other = CreateProvider(new GraphicsTestState());
        IGraphicsDeviceService registered = provider.GetRequiredService<IGraphicsDeviceService>();
        IGraphicsDeviceService foreign = other.GetRequiredService<IGraphicsDeviceService>();
        await registered.InitializeAsync();
        await foreign.InitializeAsync();
        Assert.Equal(registered.State.Version, foreign.State.Version);
        state.OverrideService = foreign;
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => resources.AcquireLeaseForTestAsync(_gpuKey).AsTask());
        await resources.TrimAsync();
        Assert.Empty(state.Values);
        await foreign.DisposeAsync();
        Assert.Equal(GraphicsDeviceServiceStatus.Disposed, foreign.State.Status);
    }

    /// <summary>Scopes share the manager, graphics environment and device without owning their lifetime.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task ScopesShareTheApplicationGraphicsEnvironmentAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        GraphicsResourceRuntime runtime = provider.GetRequiredService<GraphicsResourceRuntime>();
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        Assert.Empty(state.Devices);
        await devices.InitializeAsync();
        using ResourceLease<GpuValue> value = await resources.AcquireLeaseForTestAsync(_gpuKey);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            Assert.Same(resources, scope.ServiceProvider.GetRequiredService<ResourceManager>());
            Assert.Same(runtime, scope.ServiceProvider.GetRequiredService<GraphicsResourceRuntime>());
            Assert.Same(devices, scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>());
        }

        Assert.True(value.IsUsable);
        Assert.Equal(0, state.Devices[0].Disposals);
        await using AsyncServiceScope other = provider.CreateAsyncScope();
        Assert.Same(runtime, other.ServiceProvider.GetRequiredService<GraphicsResourceRuntime>());
        Assert.Same(resources, other.ServiceProvider.GetRequiredService<ResourceManager>());
    }

    /// <summary>Bridge registration requires a singleton device and manager and one runtime per device service.</summary>
    [Fact]
    public void InvalidBridgeRegistrationsAreRejected()
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddSingleton(new GraphicsTestState());
        services.AddResources(builder => builder.UseSource<TestSource>().AddSourcePipeline<CpuValue, CpuStage>());
        Assert.Throws<ArgumentException>(() => services.AddResourceGraphics());
        Assert.Throws<ArgumentException>(() => services.AddResourceGraphics("missing"));
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        services.AddGraphicsDevice<TestFactory>("scoped");
        Assert.Throws<ArgumentException>(() => services.AddResourceGraphics("scoped"));
        var withoutManager = new ServiceCollection();
        withoutManager.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        Assert.Throws<ArgumentException>(() => withoutManager.AddResourceGraphics());
        services.AddResourceGraphics();
        Assert.ThrowsAny<ArgumentException>(() => services.AddResourceGraphics());
        var scoped = new ServiceCollection();
        scoped.AddResources(builder => builder.UseSource<TestSource>().AddSourcePipeline<CpuValue, CpuStage>());
        scoped.AddGraphicsDevice<TestFactory>();
        Assert.Throws<ArgumentException>(() => scoped.AddResourceGraphics());
    }

    private static ServiceProvider CreateProvider(GraphicsTestState state, bool separateReadback = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        if (separateReadback)
        {
            services.AddGraphicsDevice<TestFactory>("readback", ServiceLifetime.Singleton);
        }

        services.AddResources(builder => builder
            .UseSource<TestSource>()
            .AddSourcePipeline<CpuValue, CpuStage>()
            .AddTransformPipeline<CpuValue, GpuValue, GpuStage>()
            .AddSourcePipeline<ReadbackValue, ReadbackStage>()
            .UseStageRuntime<GpuValue, GraphicsResourceRuntime>()
            .UseStageRuntime<ReadbackValue>(provider => separateReadback
                ? provider.GetResourceGraphicsRuntime("readback")
                : provider.GetRequiredService<GraphicsResourceRuntime>()));
        services.AddResourceGraphics();
        if (separateReadback)
        {
            services.AddResourceGraphics("readback");
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
