using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class NativeResources
{
    private readonly List<IDisposable> _owned = [];

    internal static NativeResources Attach(ResourceBuildContext context, IGraphicsDeviceService devices, GraphicsDeviceLease lease, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident)
    {
        IGraphicDevice device = lease.Device;
        var native = new NativeResources();
        context.RegisterCleanup(
            async () =>
            {
                if (device.Status == GraphicsDeviceStatus.Lost)
                {
                    await devices.WaitForRetirementAsync(lease.State.Epoch);
                }

                // Remove only successfully released entries, allowing the manager to retry cleanup.
                while (native._owned.Count > 0)
                {
                    native._owned[^1].Dispose();
                    native._owned.RemoveAt(native._owned.Count - 1);
                }
            },
            lifetime);
        return native;
    }

    internal T Own<T>(T value)
        where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }
}
