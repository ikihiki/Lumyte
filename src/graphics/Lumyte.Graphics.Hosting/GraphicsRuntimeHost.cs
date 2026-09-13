using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Lumyte.Graphics.Hosting;

internal sealed class GraphicsRuntimeHost : IGpuGraphicsSessionAccessor, IAsyncDisposable, IDisposable
{
    private readonly object gate = new();
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IOptions<GpuGraphicsOptions> options;
    private readonly IGpuRenderProviderDefinition[] definitions;
    private readonly CancellationTokenSource initialization = new();
    private readonly CancellationTokenRegistration applicationStopping;
    private readonly TaskCompletionSource<GpuGraphicsSession> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AsyncServiceScope? scope;
    private IGpuRenderRuntime? runtime;
    private IGpuGraphicsPresentationConnection? presentation;
    private GraphicsPresentationGate? presentationGate;
    private GpuRenderContext? renderContext;
    private Task? startTask;
    private Task? stopTask;
    private bool stopping;

    public GraphicsRuntimeHost(IServiceScopeFactory scopeFactory, IOptions<GpuGraphicsOptions> options,
        IEnumerable<IGpuRenderProviderDefinition> definitions, IHostApplicationLifetime lifetime)
    {
        this.scopeFactory = scopeFactory;
        this.options = options;
        this.definitions = definitions.ToArray();
        applicationStopping = lifetime.ApplicationStopping.Register(Close);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(initialization.Cancel);
        await EnsureStarted().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<GpuGraphicsSession> GetAsync(CancellationToken cancellationToken = default)
    {
        _ = EnsureStarted();
        return new(ready.Task.WaitAsync(cancellationToken));
    }

    private Task EnsureStarted()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            return startTask ??= InitializeAsync(initialization.Token);
        }
    }

    public void Close()
    {
        lock (gate)
        {
            if (stopping)
            {
                return;
            }
            stopping = true;
            presentationGate?.Close();
            runtime?.StopAccepting();
            initialization.Cancel();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Close();
        lock (gate)
        {
            stopTask ??= ShutdownAsync();
            return stopTask.WaitAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        scope = scopeFactory.CreateAsyncScope();
        try
        {
            GpuRenderRuntimeOptions configured = options.Value.Runtime;
            var snapshot = configured with { RequiredPasses = Array.AsReadOnly(configured.RequiredPasses.ToArray()) };
            var registry = new GpuRenderProviderRegistry();
            foreach (IGpuRenderProviderDefinition definition in definitions)
            {
                registry.Register(await definition.CreateAsync(scope.Value.ServiceProvider, cancellationToken).ConfigureAwait(false));
            }
            runtime = await registry.CreateAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (scope.Value.ServiceProvider.GetService<IGpuGraphicsPresentationFactory>() is { } factory)
            {
                presentation = await factory.CreateAsync(runtime, cancellationToken).ConfigureAwait(false);
                presentationGate = new GraphicsPresentationGate(presentation.Presentation);
                renderContext = new GpuRenderContext(runtime, presentationGate);
            }
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(stopping, this);
                ready.TrySetResult(new GpuGraphicsSession(runtime, renderContext));
            }
        }
        catch (Exception exception)
        {
            try
            {
                await CleanupAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                exception = new AggregateException(exception, cleanupException);
            }
            ready.TrySetException(exception);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    private async Task ShutdownAsync()
    {
        if (startTask is not null)
        {
            try
            {
                await startTask.ConfigureAwait(false);
            }
            catch
            {
                // Initialization reported the failure and attempted cleanup; retry remaining ownership below.
            }
        }
        else
        {
            ready.TrySetException(new ObjectDisposedException(nameof(GraphicsRuntimeHost)));
        }
        await CleanupAsync().ConfigureAwait(false);
        applicationStopping.Dispose();
    }

    private async Task CleanupAsync()
    {
        presentationGate?.Close();
        runtime?.StopAccepting();
        if (runtime is not null)
        {
            await runtime.WaitIdleAsync().ConfigureAwait(false);
        }
        renderContext?.Dispose();
        renderContext = null;
        if (presentationGate is not null)
        {
            await presentationGate.DrainAsync().ConfigureAwait(false);
            presentationGate = null;
        }
        if (presentation is not null)
        {
            await presentation.DisposeAsync().ConfigureAwait(false);
            presentation = null;
        }
        if (runtime is not null)
        {
            // A failure does not prove GPU usage ended: retain the remaining objects and CPU scope.
            await runtime.DisposeAsync().ConfigureAwait(false);
            runtime = null;
        }
        if (scope is AsyncServiceScope ownedScope)
        {
            await ownedScope.DisposeAsync().ConfigureAwait(false);
            scope = null;
        }
    }
}
