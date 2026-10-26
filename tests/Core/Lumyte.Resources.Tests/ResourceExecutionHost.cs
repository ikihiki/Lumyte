namespace Lumyte.Resources.Tests;

internal sealed class ResourceExecutionHost : IAsyncDisposable
{
    internal ResourceExecutionHost()
        : this(new ResourceRuntime())
    {
    }

    internal ResourceExecutionHost(IServiceProvider services)
        : this(services.GetRequiredService<ExecutionRuntime>(), services.GetRequiredService<ResourceRuntime>())
    {
    }

    private ResourceExecutionHost(ResourceRuntime resources)
        : this(new ExecutionRuntime([resources]), resources)
    {
    }

    private ResourceExecutionHost(ExecutionRuntime runtime, ResourceRuntime resources)
    {
        Runtime = runtime;
        Resources = resources;
        Start();
    }

    internal ResourceRuntime Resources { get; }

    internal ExecutionRuntime Runtime { get; }

    public ValueTask DisposeAsync() => Runtime.StopAsync();

    private void Start()
    {
        Runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
        Runtime.StartAsync().GetAwaiter().GetResult();
    }
}
