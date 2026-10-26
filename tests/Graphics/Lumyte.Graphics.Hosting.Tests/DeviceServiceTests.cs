namespace Lumyte.Graphics.Hosting.Tests;

/// <summary>Tests DI ownership, generation recovery, cancellation and failed cleanup.</summary>
public sealed class DeviceServiceTests
{
    /// <summary>Concurrent creation is shared while each wait can be cancelled independently.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task CreationIsSharedAndCancellationDoesNotCancelOtherWaitersAsync()
    {
        var state = new TestFactoryState { CreationWait = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using ServiceProvider provider = CreateProvider(state);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService service = scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        using var cancelled = new CancellationTokenSource();
        Task first = service.InitializeAsync(cancelled.Token).AsTask();
        Task second = service.InitializeAsync().AsTask();
        Assert.Equal(1, state.Calls);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DisposeAsync().AsTask());
        state.CreationWait.SetResult();
        await second;
        Assert.Equal(GraphicsDeviceServiceStatus.Ready, service.State.Status);
        using GraphicsDeviceLease lease = await service.AcquireAsync();
        Assert.Same(state.Devices[0], lease.Device);
    }

    /// <summary>A loss invalidates old leases while recovery preserves ownership of the old epoch.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task LossAndRecoveryPreserveExactEpochOwnershipAsync()
    {
        var state = new TestFactoryState();
        await using ServiceProvider provider = CreateProvider(state);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService service = scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        await service.InitializeAsync();
        GraphicsDeviceLease old = await service.AcquireAsync();
        ulong readyVersion = old.State.Version;
        int notifications = 0;
        service.StateChanged += (_, _) => throw new InvalidOperationException("Bad observer.");
        service.StateChanged += (_, _) => notifications++;
        state.Devices[0].Lose();
        state.Devices[0].Lose();
        Assert.Equal(1, notifications);
        Assert.False(old.IsUsable);
        Assert.Throws<GraphicsDeviceLostException>(() => old.Device);
        Assert.Equal(readyVersion, service.State.InvalidatedThroughVersion);
        await Assert.ThrowsAsync<GraphicsDeviceUnavailableException>(() => service.AcquireAsync().AsTask());
        await service.WaitForRetirementAsync(old.State.Epoch);
        Assert.Equal(0, await service.RetireAsync());
        Assert.Equal(0, state.Devices[0].Disposals);
        state.CreationWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<GraphicsDeviceState> recovery = service.RecoverAsync().AsTask();
        Task<GraphicsDeviceState> shared = service.RecoverAsync().AsTask();
        Assert.Equal(2, state.Calls);
        state.CreationWait.SetResult();
        await Task.WhenAll(recovery, shared);
        using GraphicsDeviceLease current = await service.AcquireAsync();
        Assert.Same(state.Devices[1], current.Device);
        Assert.True(current.State.Epoch > old.State.Epoch);
        Assert.True(current.State.Version > readyVersion);
        Assert.Equal(readyVersion, current.State.InvalidatedThroughVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DisposeAsync().AsTask());
        old.Dispose();
        old.Dispose();
        Assert.Equal(1, await service.RetireAsync());
        Assert.Equal(1, state.Devices[0].Disposals);
    }

    /// <summary>Concurrent returns of one lease cannot release another lease's ownership.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task ConcurrentDisposalReturnsAnEpochLeaseOnlyOnceAsync()
    {
        var state = new TestFactoryState();
        await using ServiceProvider provider = CreateProvider(state);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService service = scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        await service.InitializeAsync();
        GraphicsDeviceLease returned = await service.AcquireAsync();
        using GraphicsDeviceLease retained = await service.AcquireAsync();
        state.Devices[0].Lose();

        Parallel.For(0, 64, _ => returned.Dispose());

        Assert.False(returned.IsUsable);
        Assert.Throws<ObjectDisposedException>(() => returned.Device);
        Assert.Equal(0, await service.RetireAsync());
        Assert.Equal(0, state.Devices[0].Disposals);
        retained.Dispose();
        Assert.Equal(1, await service.RetireAsync());
        Assert.Equal(1, state.Devices[0].Disposals);
    }

    /// <summary>Failed cleanup remains owned and is retried without losing its diagnosis.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task FailedRetirementAndCleanupRemainOwnedAsync()
    {
        var state = new TestFactoryState();
        await using ServiceProvider provider = CreateProvider(state);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService service = scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        await service.InitializeAsync();
        state.Devices[0].Lose();
        state.Devices[0].RejectRetirement = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetireAsync().AsTask());
        Assert.Equal(0, state.Devices[0].Disposals);
        state.Devices[0].RejectRetirement = false;
        state.Devices[0].DisposeFailures = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetireAsync().AsTask());
        Assert.Equal(1, await service.RetireAsync());
        Assert.Equal(1, state.Devices[0].Disposals);
        await service.RecoverAsync();
        Assert.Equal(GraphicsDeviceServiceStatus.Ready, service.State.Status);
    }

    /// <summary>Cleanup does not access a disposed owner or dispose a released epoch twice.</summary>
    /// <param name="loseDevice">Whether the epoch is retired after a loss before the scope closes.</param>
    /// <returns>Completion of the scenario.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerMayRejectDeviceAccessAfterDisposalAsync(bool loseDevice)
    {
        var state = new TestFactoryState { WrapDeviceOwner = true };
        await using ServiceProvider provider = CreateProvider(state);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService service = scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        await service.InitializeAsync();
        using (GraphicsDeviceLease lease = await service.AcquireAsync())
        {
            Assert.Same(state.Devices[0], lease.Device);
        }

        if (loseDevice)
        {
            state.Devices[0].Lose();
            await service.WaitForRetirementAsync(service.State.Epoch);
            Assert.Equal(1, await service.RetireAsync());
            Assert.Equal(0, await service.RetireAsync());
        }

        await scope.DisposeAsync();
        Assert.Equal(GraphicsDeviceServiceStatus.Disposed, service.State.Status);
        Assert.Equal(1, state.Devices[0].Disposals);
        await service.DisposeAsync();
        Assert.Equal(1, state.Devices[0].Disposals);
    }

    /// <summary>Failed factories and already lost returned devices never become Ready.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task FactoryFailuresAndLostCandidatesCanBeRecoveredAsync()
    {
        var state = new TestFactoryState { Failures = 1 };
        await using ServiceProvider provider = CreateProvider(state);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService service = scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeAsync().AsTask());
        Assert.Equal(GraphicsDeviceServiceStatus.Failed, service.State.Status);
        state.ReturnLost = true;
        await Assert.ThrowsAsync<GraphicsDeviceUnavailableException>(() => service.RecoverAsync().AsTask());
        Assert.Equal(GraphicsDeviceServiceStatus.Failed, service.State.Status);
        state.ReturnLost = false;
        await service.RecoverAsync();
        Assert.Equal(1, await service.RetireAsync());
        Assert.Equal(3, state.Calls);
    }

    /// <summary>Registration names and DI scopes create distinct physical generations.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task NamesAndScopesAreIndependentAsync()
    {
        var state = new TestFactoryState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddGraphicsDevice<TestFactory>("a").AddGraphicsDevice<TestFactory>("b");
        Assert.Throws<ArgumentException>(() => services.AddGraphicsDevice<TestFactory>("a"));
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope first = provider.CreateAsyncScope();
        await using AsyncServiceScope second = provider.CreateAsyncScope();
        IGraphicsDeviceService a = first.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("a");
        IGraphicsDeviceService b = first.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("b");
        IGraphicsDeviceService other = second.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("a");
        await a.InitializeAsync();
        await b.InitializeAsync();
        await other.InitializeAsync();
        using GraphicsDeviceLease one = await a.AcquireAsync();
        using GraphicsDeviceLease two = await b.AcquireAsync();
        using GraphicsDeviceLease three = await other.AcquireAsync();
        Assert.NotSame(one.Device, two.Device);
        Assert.NotSame(one.Device, three.Device);
    }

    /// <summary>The default context remains distinct from a device named default and from other scopes.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task DefaultAndNamedContextsHaveIndependentOwnershipAsync()
    {
        var state = new TestFactoryState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddGraphicsDevice<TestFactory>("default");
        services.AddGraphicsDevice<TestFactory>();
        Assert.Throws<ArgumentException>(() => services.AddGraphicsDevice<TestFactory>());
        Assert.Throws<ArgumentException>(() => services.AddGraphicsDevice<TestFactory>(string.Empty));
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope first = provider.CreateAsyncScope();
        await using AsyncServiceScope second = provider.CreateAsyncScope();
        IGraphicsDeviceService unnamed = first.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        IGraphicsDeviceService named = first.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("default");
        IGraphicsDeviceService other = second.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        Assert.Empty(unnamed.State.Name);
        Assert.Equal("default", named.State.Name);
        Assert.NotSame(unnamed, named);
        Assert.NotSame(unnamed, other);
        await unnamed.InitializeAsync();
        await named.InitializeAsync();
        await other.InitializeAsync();
        await first.DisposeAsync();
        Assert.Equal(GraphicsDeviceServiceStatus.Disposed, unnamed.State.Status);
        Assert.Equal(GraphicsDeviceServiceStatus.Disposed, named.State.Status);
        Assert.Equal(GraphicsDeviceServiceStatus.Ready, other.State.Status);
        Assert.Equal(1, state.Devices[0].Disposals);
        Assert.Equal(1, state.Devices[1].Disposals);
        Assert.Equal(0, state.Devices[2].Disposals);
    }

    /// <summary>Application-wide device registration survives child scope disposal.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task SingletonRegistrationSharesTheDeviceAcrossScopesAsync()
    {
        var state = new TestFactoryState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Singleton);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        IGraphicsDeviceService device = provider.GetRequiredService<IGraphicsDeviceService>();
        await device.InitializeAsync();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            Assert.Same(device, scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>());
        }

        Assert.Equal(GraphicsDeviceServiceStatus.Ready, device.State.Status);
        Assert.Equal(0, state.Devices[0].Disposals);
        using GraphicsDeviceLease lease = await device.AcquireAsync();
        Assert.Same(state.Devices[0], lease.Device);
    }

    /// <summary>Transient device ownership is rejected before adding any registrations.</summary>
    [Fact]
    public void TransientRegistrationIsRejected()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddGraphicsDevice<TestFactory>("main", ServiceLifetime.Transient));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddGraphicsDevice<TestFactory>(ServiceLifetime.Transient));
        Assert.Empty(services);
    }

    /// <summary>Confirming retirement pins the owner even if another caller tries to reclaim it.</summary>
    /// <returns>Completion of the scenario.</returns>
    [Fact]
    public async Task RetirementWaitRetainsOwnerUntilItCompletesAsync()
    {
        var state = new TestFactoryState();
        await using ServiceProvider provider = CreateProvider(state);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGraphicsDeviceService service = scope.ServiceProvider.GetRequiredService<IGraphicsDeviceService>();
        await service.InitializeAsync();
        using GraphicsDeviceLease lease = await service.AcquireAsync();
        state.Devices[0].RetirementWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Devices[0].Lose();
        Task wait = service.WaitForRetirementAsync(lease.State.Epoch).AsTask();
        lease.Dispose();
        Assert.Equal(0, await service.RetireAsync());
        state.Devices[0].RetirementWait!.SetResult();
        await wait;
        Assert.Equal(1, await service.RetireAsync());
    }

    private static ServiceProvider CreateProvider(TestFactoryState state)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddGraphicsDevice<TestFactory>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
