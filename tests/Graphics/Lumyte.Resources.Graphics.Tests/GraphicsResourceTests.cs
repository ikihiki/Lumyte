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
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService devices = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await devices.InitializeAsync();
        ResourceManager resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
        using ResourceLease<CpuValue> cpu = await resources.AcquireLeaseForTestAsync(_cpuKey);
        using ResourceReference<GpuValue> reference = await resources.AcquireAsync(_gpuKey);
        using ResourceLease<GpuValue> old = await resources.AcquireLeaseForTestAsync(_gpuKey);
        GpuValue oldValue = old.Value;
        Assert.Same(scope.ServiceProvider.GetResourceGraphicsRuntime("scene", "graphics.main"), oldValue.Runtime);
        state.Devices[0].Lose();
        Assert.False(old.IsUsable);
        Assert.Throws<ResourceUnavailableException>(() => old.Value);
        Assert.False(resources.TryAcquireLeaseForTest(reference.Handle, out _));
        Assert.True(cpu.IsUsable);
        await devices.RecoverAsync();
        ResourceReloadResult reload = await resources.RebuildRuntimeAsync(["graphics.main"]);
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
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService devices = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await devices.InitializeAsync();
        ResourceManager resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
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
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService main = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        IGraphicsDeviceService readback = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("readback");
        await main.InitializeAsync();
        await readback.InitializeAsync();
        ResourceManager resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
        using ResourceLease<GpuValue> gpu = await resources.AcquireLeaseForTestAsync(_gpuKey);
        using ResourceLease<ReadbackValue> cpu = await resources.AcquireLeaseForTestAsync(_readbackKey);
        Assert.Same(main, gpu.Value.Runtime.Identity);
        Assert.Same(readback, cpu.Value.Runtime.Identity);
        Assert.Same(scope.ServiceProvider.GetResourceGraphicsRuntime("scene", "graphics.main"), gpu.Value.Runtime);
        Assert.Same(scope.ServiceProvider.GetResourceGraphicsRuntime("scene", "graphics.readback"), cpu.Value.Runtime);
        Assert.Same(provider.GetRequiredService<IResourceRuntime>(), state.CpuRuntime);

        state.Devices[0].Lose();
        Assert.False(gpu.IsUsable);
        Assert.True(cpu.IsUsable);
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
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService devices = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await devices.InitializeAsync();
        ResourceManager resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
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
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService devices = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await devices.InitializeAsync();
        ResourceManager resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
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

    /// <summary>Matching names and versions from another DI scope cannot identify the registered device.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task ForeignServiceWithTheSameVersionIsRejectedWithoutLeakingItsLeaseAsync()
    {
        var state = new GraphicsTestState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await using AsyncServiceScope other = provider.CreateAsyncScope();
        IGraphicsDeviceService registered = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        IGraphicsDeviceService foreign = other.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await registered.InitializeAsync();
        await foreign.InitializeAsync();
        Assert.Equal(registered.State.Version, foreign.State.Version);
        state.OverrideService = foreign;
        ResourceManager resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => resources.AcquireLeaseForTestAsync(_gpuKey).AsTask());
        await resources.TrimAsync();
        Assert.Empty(state.Values);
        await foreign.DisposeAsync();
        Assert.Equal(GraphicsDeviceServiceStatus.Disposed, foreign.State.Status);
    }

    /// <summary>Bridge registration requires a registered device and unique runtime identifier.</summary>
    [Fact]
    public void InvalidBridgeRegistrationsAreRejected()
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddSingleton(new GraphicsTestState());
        services.AddResources("scene", builder => builder.UseSource<TestSource>().AddSourcePipeline<CpuValue, CpuStage>());
        Assert.Throws<ArgumentException>(() => services.AddResourceGraphics("scene", "missing", "graphics.main"));
        services.AddGraphicsDevice<TestFactory>("main");
        services.AddResourceGraphics("scene", "main", "graphics.main");
        Assert.ThrowsAny<ArgumentException>(() => services.AddResourceGraphics("scene", "main", "graphics.main"));
    }

    private static ServiceProvider CreateProvider(GraphicsTestState state, bool separateReadback = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>("main");
        if (separateReadback)
        {
            services.AddGraphicsDevice<TestFactory>("readback");
        }

        services.AddResources("scene", builder => builder
            .UseSource<TestSource>()
            .AddSourcePipeline<CpuValue, CpuStage>()
            .AddTransformPipeline<CpuValue, GpuValue, GpuStage>()
            .AddSourcePipeline<ReadbackValue, ReadbackStage>()
            .UseStageRuntime<GpuValue>(provider => provider.GetResourceGraphicsRuntime("scene", "graphics.main"))
            .UseStageRuntime<ReadbackValue>(provider => provider.GetResourceGraphicsRuntime("scene", separateReadback ? "graphics.readback" : "graphics.main")));
        services.AddResourceGraphics("scene", "main", "graphics.main");
        if (separateReadback)
        {
            services.AddResourceGraphics("scene", "readback", "graphics.readback");
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
