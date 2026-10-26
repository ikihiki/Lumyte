using Lumyte.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class ResourceExecutionHost(ExecutionRuntime runtime, IServiceProvider services) : IAsyncDisposable
{
    public static async ValueTask<ResourceExecutionHost> StartAsync(IServiceProvider services)
    {
        ExecutionRuntime runtime = services.GetRequiredService<ExecutionRuntime>();
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        await runtime.StartAsync();
        return new(runtime, services);
    }

    public async ValueTask DisposeAsync()
    {
        await services.GetRequiredService<ResourceManager>().DisposeAsync();
        await runtime.StopAsync();
    }
}
