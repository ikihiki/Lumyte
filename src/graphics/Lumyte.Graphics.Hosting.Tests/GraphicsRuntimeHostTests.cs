using Lumyte.Graphics.RenderGraph;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Lumyte.Graphics.Hosting.Tests;

public sealed class GraphicsRuntimeHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitShutdownRetriesRemainingOwnershipAfterCleanupFails(bool retryWithDispose)
    {
        var events = new List<string>();
        var attempts = 0;
        var failure = new InvalidOperationException("runtime cleanup failed");
        var runtime = new TestRuntime(() =>
        {
            events.Add($"runtime {++attempts}");
            if (attempts == 1)
            { throw failure; }
        });
        var provider = new TestProvider("test", runtime);
        var connection = new TestPresentationConnection(() => events.Add("presentation"));
        IHost host = BuildHost(new(provider, services => services.GetRequiredService<ScopedDependency>()), services =>
        {
            services.AddScoped(_ => new ScopedDependency(() => events.Add("dependency")));
            services.AddScoped<IGpuGraphicsPresentationFactory>(_ => new TestPresentationFactory(connection));
        });
        await using var lifetime = (IAsyncDisposable)host;
        await host.StartAsync();

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StopAsync());
        Assert.Same(failure, observed);
        Assert.Equal(["presentation", "runtime 1"], events);
        if (retryWithDispose)
        { await lifetime.DisposeAsync(); }
        else
        { await host.StopAsync(); }

        Assert.Equal(["presentation", "runtime 1", "runtime 2", "dependency"], events);
    }

    [Fact]
    public async Task ConcurrentShutdownObserversShareTheRunningCleanup()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitCount = 0;
        var disposalCount = 0;
        var runtime = new TestRuntime(() => disposalCount++)
        {
            WaitIdle = () =>
            {
                Interlocked.Increment(ref waitCount);
                entered.TrySetResult();
                return new(release.Task);
            }
        };
        using IHost host = BuildHost(new(new TestProvider("test", runtime)));
        await host.StartAsync();

        Task first = host.StopAsync();
        await entered.Task;
        Task second = host.StopAsync();
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal((1, 1), (waitCount, disposalCount));
    }

    [Fact]
    public async Task ExplicitSelectionNeverPreparesUnselectedProviders()
    {
        var selected = new TestProvider("selected");
        var unselected = new TestProvider("unselected");
        using IHost host = new HostBuilder().ConfigureServices(services =>
            services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = "selected" })
                .AddProvider(new TestDefinition(unselected, _ => throw new InvalidOperationException("Unselected preparation ran.")))
                .AddProvider(new TestDefinition(selected))).Build();

        await host.StartAsync();

        Assert.Same(selected.Runtime, (await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime);
        await host.StopAsync();
    }

    [Fact]
    public async Task IdenticalProviderDefinitionIsRegisteredOnce()
    {
        var provider = new TestProvider("test");
        var definition = new TestDefinition(provider);
        using IHost host = new HostBuilder().ConfigureServices(services =>
            services.AddLumyteGraphics().AddProvider(definition).AddProvider(definition)).Build();

        await host.StartAsync();

        Assert.Equal(1, provider.CreationCount);
        await host.StopAsync();
    }

    [Fact]
    public void ConflictingProviderDefinitionIsRejectedBeforePreparation()
    {
        var services = new ServiceCollection();
        LumyteGraphicsBuilder builder = services.AddLumyteGraphics().AddProvider(new TestDefinition(new("same")));

        ArgumentException error = Assert.Throws<ArgumentException>(() => builder.AddProvider(new TestDefinition(new("same"))));

        Assert.Equal("definition", error.ParamName);
        Assert.Contains("different definition", error.Message);
    }

    [Fact]
    public async Task InvalidCacheCapacityFailsBeforeGpuPreparation()
    {
        var provider = new TestProvider("test");
        using IHost host = new HostBuilder().ConfigureServices(services =>
            services.AddLumyteGraphics(options => options.PlanCacheMaximumEntries = 0).AddProvider(new TestDefinition(provider))).Build();

        OptionsValidationException error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("PlanCacheMaximumEntries", error.Message);
        Assert.Equal(0, provider.CreationCount);
    }

    [Fact]
    public async Task RegistrationLeavesRuntimeCreationToHostStartup()
    {
        var provider = new TestProvider("native");
        using IHost host = BuildHost(new(provider));
        IGpuGraphicsSessionAccessor accessor = host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>();

        Assert.Equal(0, provider.CreationCount);
        await host.StartAsync();

        Assert.Same(provider.Runtime, (await accessor.GetAsync()).Runtime);
        Assert.Equal(1, provider.CreationCount);
        await host.StopAsync();
    }

    [Fact]
    public async Task ConfigurationSelectsOnlyOneProviderRuntime()
    {
        var native = new TestProvider("native");
        var portable = new TestProvider("portable");
        using IHost host = new HostBuilder().ConfigureServices(services =>
            services.AddLumyteGraphics(options => options.Runtime = new() { ProviderId = "portable" })
                .AddProvider(new TestDefinition(native))
                .AddProvider(new TestDefinition(portable))).Build();

        await host.StartAsync();
        IGpuRenderRuntime runtime = (await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync()).Runtime;

        Assert.Same(portable.Runtime, runtime);
        Assert.Equal(0, native.CreationCount);
        await host.StopAsync();
    }

    [Fact]
    public async Task ShutdownDisposesRuntimeBeforeItsScopedDependencies()
    {
        var events = new List<string>();
        var provider = new TestProvider("test", new TestRuntime(() => events.Add("runtime")));
        using IHost host = BuildHost(new(provider, services => services.GetRequiredService<ScopedDependency>()),
            services => services.AddScoped(_ => new ScopedDependency(() => events.Add("dependency"))));
        await host.StartAsync();

        await host.StopAsync();

        Assert.Equal(["runtime", "dependency"], events);
    }

    [Fact]
    public async Task StartupFailureReleasesItsScopedDependencies()
    {
        var events = new List<string>();
        var provider = new TestProvider("test") { Failure = new InvalidOperationException("creation failed") };
        using IHost host = BuildHost(new(provider, services => services.GetRequiredService<ScopedDependency>()),
            services => services.AddScoped(_ => new ScopedDependency(() => events.Add("dependency"))));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Equal("creation failed", exception.Message);
        Assert.Equal(["dependency"], events);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync().AsTask());
    }

    [Fact]
    public async Task CancelingAReadyObserverLeavesStartupRunning()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TestProvider("test") { Initialization = gate.Task };
        using IHost host = BuildHost(new(provider));
        Task startup = host.StartAsync();
        await provider.Entered.Task;
        IGpuGraphicsSessionAccessor accessor = host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>();
        using var cancellation = new CancellationTokenSource();

        Task<GpuGraphicsSession> observer = accessor.GetAsync(cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer);
        gate.SetResult();
        await startup;
        Assert.Same(provider.Runtime, (await accessor.GetAsync()).Runtime);
        await host.StopAsync();
    }

    [Fact]
    public async Task StartupCopiesMutableConfigurationBeforeAsyncPreparation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requiredPasses = new List<GpuRenderPassId>();
        var configured = new GpuGraphicsOptions { Runtime = new() { ProviderId = "test", EnableValidation = true, RequiredPasses = requiredPasses } };
        var required = new GpuRenderPassId("clear", 1);
        requiredPasses.Add(required);
        var provider = new TestProvider("test") { Initialization = gate.Task };
        using IHost host = BuildHost(new(provider), services => services.AddSingleton<IOptions<GpuGraphicsOptions>>(Options.Create(configured)));
        Task startup = host.StartAsync();
        await provider.Entered.Task;

        requiredPasses.Clear();
        configured.Runtime = configured.Runtime with { ProviderId = "changed" };
        gate.SetResult();
        await startup;

        Assert.Equal("test", provider.Options!.ProviderId);
        Assert.Equal(required, Assert.Single(provider.Options.RequiredPasses));
        await host.StopAsync();
    }

    [Fact]
    public async Task StoppedHostRejectsNewRuntimeBorrowers()
    {
        using IHost host = BuildHost(new(new TestProvider("test")));
        await host.StartAsync();
        IGpuGraphicsSessionAccessor accessor = host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>();

        await host.StopAsync();

        Assert.Throws<ObjectDisposedException>(() => accessor.GetAsync());
    }

    [Fact]
    public async Task EarlierConsumerCanInitializeGraphicsDuringItsOwnStartup()
    {
        var provider = new TestProvider("test");
        var consumer = new StartupConsumer();
        using IHost host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IHostedService>(services =>
            {
                consumer.Accessor = services.GetRequiredService<IGpuGraphicsSessionAccessor>();
                return consumer;
            });
            services.AddLumyteGraphics().AddProvider(new TestDefinition(provider));
        }).Build();

        await host.StartAsync();

        Assert.Same(provider.Runtime, consumer.Runtime);
        Assert.Equal(1, provider.CreationCount);
        await host.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConsumerStopFinishesBeforeGraphicsDrainRegardlessOfRegistrationOrder(bool graphicsFirst)
    {
        var events = new List<string>();
        var provider = new TestProvider("test", new TestRuntime(() => events.Add("runtime")));
        using IHost host = new HostBuilder().ConfigureServices(services =>
        {
            if (graphicsFirst)
            {
                services.AddLumyteGraphics().AddProvider(new TestDefinition(provider));
            }
            services.AddSingleton<IHostedService>(new StopConsumer(() => events.Add("consumer")));
            if (!graphicsFirst)
            {
                services.AddLumyteGraphics().AddProvider(new TestDefinition(provider));
            }
        }).Build();
        await host.StartAsync();

        await host.StopAsync();

        Assert.Equal(["consumer", "runtime"], events);
    }

    [Fact]
    public async Task ShutdownEndsPresentationBeforeRuntimeAndScopedDependencies()
    {
        var events = new List<string>();
        var provider = new TestProvider("test", new TestRuntime(() => events.Add("runtime")));
        var connection = new TestPresentationConnection(() => events.Add("presentation"));
        using IHost host = BuildHost(new(provider, services => services.GetRequiredService<ScopedDependency>()), services =>
        {
            services.AddScoped(_ => new ScopedDependency(() => events.Add("dependency")));
            services.AddScoped<IGpuGraphicsPresentationFactory>(_ => new TestPresentationFactory(connection));
        });
        await host.StartAsync();
        GpuGraphicsSession session = await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync();
        Assert.NotNull(session.RenderContext);

        await host.StopAsync();

        Assert.Equal(["presentation", "runtime", "dependency"], events);
    }

    private static IHost BuildHost(TestDefinition definition, Action<IServiceCollection>? configure = null) =>
        new HostBuilder().ConfigureServices(services =>
        {
            services.AddLumyteGraphics().AddProvider(definition);
            configure?.Invoke(services);
        }).Build();

    [Fact]
    public async Task ShutdownReturnsAnInFlightTargetBeforeClosingItsConnection()
    {
        var events = new List<string>();
        var acquired = new TaskCompletionSource<GpuGraphPresentationTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TestProvider("test");
        var connection = new TestPresentationConnection(() => events.Add("connection"), acquired.Task,
            () => events.Add("target"));
        using IHost host = BuildHost(new(provider), services =>
            services.AddScoped<IGpuGraphicsPresentationFactory>(_ => new TestPresentationFactory(connection)));
        await host.StartAsync();
        GpuGraphicsSession session = await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync();
        Task<GpuFrame> frame = session.RenderContext!.BeginFrameAsync().AsTask();

        Task stopped = host.StopAsync();
        Assert.False(stopped.IsCompleted);
        acquired.SetResult(new(new TestTextureRef(provider.Runtime.Id), new ScopedDependency(() => { })));

        await Assert.ThrowsAsync<ObjectDisposedException>(() => frame);
        await stopped;
        Assert.Equal(["target", "connection"], events);
    }

    [Fact]
    public async Task ShutdownCancelsPendingSurfaceAcquisition()
    {
        var surface = new WaitingSurface();
        using IHost host = new HostBuilder().ConfigureServices(services =>
            services.AddLumyteGraphics().AddProvider(new TestDefinition(new TestProvider("test")))
                .UsePresentation((_, _, _) => new(new GpuGraphicsSurfaceConnection(surface)))).Build();
        await host.StartAsync();
        var session = await host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>().GetAsync();
        var frame = session.RenderContext!.BeginFrameAsync().AsTask();
        await surface.Started.Task;

        Task stopped = host.StopAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => frame);
        await stopped;

        Assert.True(surface.Disposed);
    }
    private sealed class WaitingSurface : GpuSurfacePresentation
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<GpuGraphPresentationTarget> target = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        protected override ValueTask<GpuGraphPresentationTarget> AcquireCoreAsync(CancellationToken cancellationToken)
        { Started.TrySetResult(); return new(target.Task.WaitAsync(cancellationToken)); }
        protected override ValueTask ReturnCoreAsync(GpuGraphPresentationTarget target, bool present) => throw new InvalidOperationException("No image was acquired.");
        protected override ValueTask DisposeCoreAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class TestDefinition(TestProvider provider, Action<IServiceProvider>? prepare = null) : IGpuRenderProviderDefinition
    {
        public string Id => provider.Id;
        public ValueTask<IGpuRenderProvider> CreateAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            prepare?.Invoke(services);
            return ValueTask.FromResult<IGpuRenderProvider>(provider);
        }
    }

    private sealed class TestProvider(string id, TestRuntime? runtime = null) : IGpuRenderProvider
    {
        public string Id { get; } = id;
        public int ContractVersion => 1;
        public TestRuntime Runtime { get; } = runtime ?? new TestRuntime();
        public int CreationCount { get; private set; }
        public Task? Initialization { get; init; }
        public Exception? Failure { get; init; }
        public GpuRenderRuntimeOptions? Options { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IGpuRenderRuntime> CreateAsync(GpuRenderRuntimeOptions options, CancellationToken cancellationToken = default)
        {
            CreationCount++;
            Options = options;
            Entered.SetResult();
            if (Initialization is not null)
            {
                await Initialization.WaitAsync(cancellationToken);
            }
            if (Failure is not null)
            {
                throw Failure;
            }
            return Runtime;
        }
    }

    private sealed class TestRuntime(Action? onDispose = null) : IGpuRenderRuntime
    {
        public Func<ValueTask>? WaitIdle { get; init; }
        public Guid Id { get; } = Guid.NewGuid();
        public IGpuGraphResources Resources => throw new NotSupportedException();
        public ValueTask<GpuRenderGraphExecution> SubmitAsync(GpuRenderGraphPlan plan, GpuRenderGraphBindings? bindings = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void StopAccepting() { }
        public ValueTask WaitIdleAsync(CancellationToken cancellationToken = default) => WaitIdle?.Invoke() ?? ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            onDispose?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScopedDependency(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    private sealed class StartupConsumer : IHostedService
    {
        public IGpuGraphicsSessionAccessor Accessor { get; set; } = null!;
        public IGpuRenderRuntime? Runtime { get; private set; }
        public async Task StartAsync(CancellationToken cancellationToken) => Runtime = (await Accessor.GetAsync(cancellationToken)).Runtime;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StopConsumer(Action stop) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) { stop(); return Task.CompletedTask; }
    }

    private sealed class TestPresentationFactory(IGpuGraphicsPresentationConnection connection) : IGpuGraphicsPresentationFactory
    {
        public ValueTask<IGpuGraphicsPresentationConnection> CreateAsync(IGpuRenderRuntime runtime, CancellationToken cancellationToken) =>
            ValueTask.FromResult(connection);
    }

    private sealed class TestPresentationConnection(Action onDispose,
        Task<GpuGraphPresentationTarget>? acquired = null, Action? onDiscard = null) : IGpuGraphicsPresentationConnection, IGpuGraphPresentation
    {
        public IGpuGraphPresentation Presentation => this;
        public ValueTask DisposeAsync() { onDispose(); return ValueTask.CompletedTask; }
        // Models acquisition already accepted by the native surface: cancellation cannot
        // retract it, so Host must still discard the image that eventually arrives.
        public ValueTask<GpuGraphPresentationTarget> AcquireNextTargetAsync(CancellationToken cancellationToken = default) =>
            acquired is null ? throw new NotSupportedException() : new(acquired);
        public void Present(GpuGraphPresentationTarget target, GpuGraphCompletion completion) => throw new NotSupportedException();
        public void Retire(GpuGraphPresentationTarget target, GpuGraphCompletion completion) => throw new NotSupportedException();
        public void Discard(GpuGraphPresentationTarget target) { target.Ownership.Dispose(); onDiscard?.Invoke(); }
    }

    private sealed class TestTextureRef(Guid runtimeId) : GpuGraphTextureRef(runtimeId, Guid.NewGuid(), new(1, 1, GpuFormat.Rgba8Unorm));
}
