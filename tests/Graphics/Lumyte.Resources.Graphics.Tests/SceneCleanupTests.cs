using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Gltf.Sample;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Graphics.Tests;

/// <summary>Exercises the scene sample's actual cleanup through resource and device lifetimes.</summary>
public sealed class SceneCleanupTests
{
    private static readonly ResourceKey<SceneOutput> _key = new("scene");

    /// <summary>Build temporaries are returned before publication and resident children retain their original epoch.</summary>
    /// <param name="loseDevice">Whether the original device is lost and replaced before cleanup.</param>
    /// <returns>Completion of the scenario.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildrenUseTheirOriginalDeviceUntilCleanupCompletesAsync(bool loseDevice)
    {
        var state = new CleanupState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await devices.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        using ResourceLease<SceneOutput> lease = await resources.AcquireLeaseForTestAsync(_key);
        SceneOutput output = lease.Value;
        Assert.Equal(["upload"], state.Released);
        if (loseDevice)
        {
            output.Device.Lose();
            await devices.RecoverAsync();
            Assert.Throws<GraphicsDeviceLostException>(() => output.Lease.Device);
            Assert.Equal(0, await devices.RetireAsync());
        }

        lease.Dispose();
        await resources.TrimAsync();
        Assert.Equal(["upload", "second", "first"], state.Released);
        Assert.Equal(loseDevice ? 1 : 0, output.Device.Confirmations);
        Assert.Equal(loseDevice ? 1 : 0, await devices.RetireAsync());
        Assert.Equal(0, state.Graphics.Devices[^1].Disposals);
        await resources.DisposeAsync();
    }

    /// <summary>Cleanup failures retain the epoch and retry only unfinished children.</summary>
    /// <param name="failRetirement">Whether retirement fails instead of the final child's disposal.</param>
    /// <returns>Completion of the scenario.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCleanupRetainsTheOldEpochForRetryAsync(bool failRetirement)
    {
        var state = new CleanupState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await devices.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        using ResourceLease<SceneOutput> lease = await resources.AcquireLeaseForTestAsync(_key);
        SceneOutput output = lease.Value;
        output.Device.Lose();
        await devices.RecoverAsync();
        output.Device.RejectRetirement = failRetirement;
        output.First.Failures = failRetirement ? 0 : 1;
        lease.Dispose();

        await Assert.ThrowsAsync<AggregateException>(() => resources.TrimAsync().AsTask());
        Assert.Equal(failRetirement ? ["upload"] : new[] { "upload", "second" }, state.Released);
        Assert.Equal(0, await devices.RetireAsync());
        Assert.Equal(0, output.Device.Disposals);
        output.Device.RejectRetirement = false;
        await resources.TrimAsync();
        Assert.Equal(["upload", "second", "first"], state.Released);
        Assert.Equal(1, output.Second.Attempts);
        Assert.Equal(1, await devices.RetireAsync());
        await resources.DisposeAsync();
    }

    /// <summary>Pending retirement prevents both native disposal and returning the old epoch lease.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task PendingRetirementKeepsChildrenAndEpochAliveAsync()
    {
        var state = new CleanupState();
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await devices.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        using ResourceLease<SceneOutput> lease = await resources.AcquireLeaseForTestAsync(_key);
        SceneOutput output = lease.Value;
        output.Device.RetirementStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        output.Device.RetirementWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        output.Device.Lose();
        lease.Dispose();
        Task cleanup = resources.TrimAsync().AsTask();
        try
        {
            await output.Device.RetirementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(cleanup.IsCompleted);
            Assert.Equal(["upload"], state.Released);
            Assert.Equal(0, await devices.RetireAsync());
        }
        finally
        {
            output.Device.RetirementWait.TrySetResult();
            await cleanup;
        }

        Assert.Equal(["upload", "second", "first"], state.Released);
        Assert.Equal(1, await devices.RetireAsync());
        await resources.DisposeAsync();
    }

    /// <summary>A lost, failed build keeps temporary and resident resources until retirement can be confirmed.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task FailedBuildRetainsAllNativeResourcesUntilRetryAsync()
    {
        var state = new CleanupState { FailBuild = true };
        await using ServiceProvider provider = CreateProvider(state);
        await using ResourceExecutionHost execution = await ResourceExecutionHost.StartAsync(provider);
        IGraphicsDeviceService devices = provider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
        await devices.InitializeAsync();
        ResourceManager resources = provider.GetRequiredService<ResourceManager>();
        await Assert.ThrowsAsync<AggregateException>(() => resources.AcquireAsync(_key).AsTask());
        TestDevice device = Assert.Single(state.Graphics.Devices);
        Assert.Empty(state.Released);
        Assert.Equal(0, await devices.RetireAsync());
        device.RejectRetirement = false;
        await resources.TrimAsync();
        Assert.Equal(["upload", "second", "first"], state.Released);
        Assert.Equal(1, await devices.RetireAsync());
        await resources.DisposeAsync();
    }

    private static ServiceProvider CreateProvider(CleanupState state)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(state.Graphics);
        services.AddMemoryManagement();
        services.AddGraphicsDevice<TestFactory>("main", ServiceLifetime.Singleton);
        services.AddResources(builder => builder.UseSource<TestSource>()
            .AddSourcePipeline<SceneOutput, SceneStage>()
            .UseStageRuntime<SceneOutput>(provider => provider.GetResourceGraphicsRuntime("graphics.main")));
        services.AddResourceGraphics("main", "graphics.main");
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class CleanupState
    {
        internal GraphicsTestState Graphics { get; } = new();

        internal List<string> Released { get; } = [];

        internal bool FailBuild { get; init; }
    }

    private sealed record SceneOutput(TestDevice Device, GraphicsDeviceLease Lease, NativeChild First, NativeChild Second);

    private sealed class SceneStage(CleanupState state) : IResourceSourceStage<SceneOutput, GraphicsResourceRuntime>
    {
        public async ValueTask<SceneOutput> BuildAsync(GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        {
            GraphicsDeviceLease lease = await runtime.AcquireAsync(context, cancellationToken: cancellationToken);
            var device = (TestDevice)lease.Device;
            var resident = NativeResources.Attach(context, runtime, lease);
            NativeChild first = resident.Own(new NativeChild("first", device, state));
            NativeChild second = resident.Own(new NativeChild("second", device, state));
            var temporary = NativeResources.Attach(context, runtime, lease, ResourceDependencyLifetime.Build);
            temporary.Own(new NativeChild("upload", device, state));
            if (state.FailBuild)
            {
                device.RejectRetirement = true;
                device.Lose();
                throw new IOException("Injected upload failure.");
            }

            return new(device, lease, first, second);
        }

        public ValueTask ReleaseAsync(SceneOutput resource, GraphicsResourceRuntime runtime) => ValueTask.CompletedTask;
    }

    private sealed class NativeChild(string name, TestDevice device, CleanupState state) : IDisposable
    {
        internal int Failures { get; set; }

        internal int Attempts { get; private set; }

        public void Dispose()
        {
            Assert.True(device.Status != GraphicsDeviceStatus.Lost || device.Confirmations > 0);
            Assert.Equal(0, device.Disposals);
            Attempts++;
            if (Failures > 0)
            {
                Failures--;
                throw new InvalidOperationException("Injected child disposal failure.");
            }

            Assert.DoesNotContain(name, state.Released);
            state.Released.Add(name);
        }
    }
}
