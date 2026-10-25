using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class GpuStage(GraphicsTestState state) : IResourceTransform<CpuValue, GpuValue, GraphicsResourceRuntime>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => output;

    public async ValueTask<GpuValue> BuildAsync(CpuValue input, GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsDeviceLease lease = state.OverrideService is { } other
            ? await context.UseGraphicsAsync(other, runtime.Id, cancellationToken: cancellationToken)
            : await runtime.AcquireAsync(context, cancellationToken: cancellationToken);
        state.GpuRuntime = runtime;
        var value = new GpuValue(lease, lease.Device);
        state.Values.Add(value);
        state.BuildStarted?.TrySetResult();
        if (state.BuildWait is { } wait)
        {
            await wait.Task.WaitAsync(cancellationToken);
        }

        return value;
    }

    public async ValueTask ReleaseAsync(GpuValue resource, GraphicsResourceRuntime runtime)
    {
        if (state.ReleaseFailures > 0)
        {
            state.ReleaseFailures--;
            throw new InvalidOperationException("Injected child release failure.");
        }

        if (resource.Device.Status == GraphicsDeviceStatus.Lost)
        {
            await runtime.WaitForRetirementAsync(resource.Lease.State.Epoch);
        }

        state.GpuReleaseRuntime = runtime;
        resource.Released = true;
    }
}
