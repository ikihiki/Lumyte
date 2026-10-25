using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Resources.Graphics;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class NativeResources(GraphicsResourceRuntime runtime, GraphicsDeviceLease lease)
{
    private readonly List<IDisposable> _owned = [];
    private readonly IGraphicDevice _device = lease.Device;

    internal static NativeResources Attach(ResourceBuildContext context, GraphicsResourceRuntime runtime, GraphicsDeviceLease lease, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident)
    {
        var native = new NativeResources(runtime, lease);
        context.RegisterCleanup(native.ReleaseAsync, lifetime);
        return native;
    }

    internal T Own<T>(T value)
        where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }

    private async ValueTask ReleaseAsync()
    {
        if (_device.Status == GraphicsDeviceStatus.Lost)
        {
            await runtime.WaitForRetirementAsync(lease.State.Epoch);
        }

        // Remove only successfully released entries, allowing the manager to retry cleanup.
        while (_owned.Count > 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }
}
