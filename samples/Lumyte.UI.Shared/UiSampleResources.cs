using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Graphics;
using Lumyte.Resources.Graphics.Drawing2D;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.SampleSupport;

internal sealed class UiSampleResources(ServiceProvider services, ExecutionRuntime execution, ResourceReference<Renderer2DResource> reference) : IAsyncDisposable
{
    internal ResourceReference<Renderer2DResource> Reference { get; } = reference;

    internal ResourceManager Manager => services.GetRequiredService<ResourceManager>();

    public async ValueTask DisposeAsync()
    {
        Reference.Dispose();
        await CompleteAsync(execution, Manager.DisposeAsync().AsTask());
        await CompleteAsync(execution, execution.StopAsync().AsTask());
        await services.DisposeAsync();
    }

    internal static async ValueTask<UiSampleResources> CreateAsync(IGraphicDevice device)
    {
        var collection = new ServiceCollection();
        collection.AddMemoryManagement();
        collection.AddGraphicsDevice<BorrowedFactory>(ServiceLifetime.Singleton);
        collection.AddSingleton(new BorrowedFactory(device));
        collection.AddSingleton(new UnusedSource());
        collection.AddResources(builder => builder.UseSource<UnusedSource>().AddGraphics2D());
        collection.AddResourceGraphics();

        // An application definition is configured before ResourceManager is constructed.
        collection.AddGraphicsResource(DrawingGraphicsResources2D.Pipeline, new GraphicsPipelineDefinition
        {
            VertexShader = DrawingGraphicsResources2D.VertexShader,
            FragmentShader = DrawingGraphicsResources2D.FragmentShader,
            Label = "ui-sample-configured-pipeline",
            Optimization = PipelineOptimizationMode.PreferReuse,
        });
        ServiceProvider services = collection.BuildServiceProvider();
        ExecutionRuntime execution = services.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, OperatingSystem.IsBrowser() ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        try
        {
            await CompleteAsync(execution, execution.StartAsync().AsTask());
            await services.GetRequiredService<IGraphicsDeviceService>().InitializeAsync();
            Task<ResourceReference<Renderer2DResource>> acquisition = services.GetRequiredService<ResourceManager>().AcquireRenderer2DAsync().AsTask();
            await CompleteAsync(execution, acquisition);
            ResourceReference<Renderer2DResource> reference = await acquisition;
            return new(services, execution, reference);
        }
        catch
        {
            await CompleteAsync(execution, services.GetRequiredService<ResourceManager>().DisposeAsync().AsTask());
            await CompleteAsync(execution, execution.StopAsync().AsTask());
            await services.DisposeAsync();
            throw;
        }
    }

    private static async Task CompleteAsync(ExecutionRuntime execution, Task operation)
    {
        while (OperatingSystem.IsBrowser() && !operation.IsCompleted)
        {
            execution.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            execution.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            await Task.Delay(1);
        }

        await operation;
    }

    private sealed class BorrowedFactory(IGraphicDevice device) : IGraphicsDeviceFactory
    {
        public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IGraphicsDeviceOwner>(new BorrowedOwner(device));
        }
    }

    private sealed class BorrowedOwner(IGraphicDevice device) : IGraphicsDeviceOwner
    {
        public IGraphicDevice Device { get; } = device;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Embedded defaults must not read the sample source.");

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => new((string?)null);
    }
}
