using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class GpuStage(GraphicsTestState state, [FromKeyedServices("main")] IGraphicsDeviceService service) : IResourceTransform<CpuValue, GpuValue>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => output;

    public async ValueTask<GpuValue> BuildAsync(CpuValue input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsDeviceLease lease = await context.UseGraphicsAsync(state.OverrideService ?? service, "graphics.main", cancellationToken: cancellationToken);
        var value = new GpuValue(lease, lease.Device);
        state.Values.Add(value);
        state.BuildStarted?.TrySetResult();
        if (state.BuildWait is { } wait)
        {
            await wait.Task.WaitAsync(cancellationToken);
        }

        return value;
    }

    public async ValueTask ReleaseAsync(GpuValue resource)
    {
        if (state.ReleaseFailures > 0)
        {
            state.ReleaseFailures--;
            throw new InvalidOperationException("Injected child release failure.");
        }

        if (resource.Device.Status == GraphicsDeviceStatus.Lost)
        {
            await service.WaitForRetirementAsync(resource.Lease.State.Epoch);
        }

        resource.Released = true;
    }
}
