using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics;

/// <summary>Records the exact graphics epoch used by a resource stage.</summary>
public static class ResourceGraphicsBuildExtensions
{
    /// <summary>Acquires a graphics epoch and transfers its lease to the build context.</summary>
    /// <param name="context">The current resource build.</param>
    /// <param name="service">The same device service borrowed by the selected graphics runtime.</param>
    /// <param name="lifetime">Whether the physical device is needed until build completion or resource release.</param>
    /// <param name="cancellationToken">Cancels before a graphics lease is acquired.</param>
    /// <returns>The borrowed lease retaining the exact device epoch; the caller must not dispose it.</returns>
    /// <remarks>The stage releases native children before context cleanup returns the lease. Prefer the selected GraphicsResourceRuntime's AcquireAsync method for stage execution.</remarks>
    public static async ValueTask<GraphicsDeviceLease> UseGraphicsAsync(this ResourceBuildContext context, IGraphicsDeviceService service, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default)
    {
        if (context.Runtime is not GraphicsResourceRuntime runtime)
        {
            throw new InvalidOperationException("Select a graphics runtime before acquiring a graphics device.");
        }

        GraphicsDeviceLease lease = await service.AcquireAsync(cancellationToken);
        try
        {
            context.TrackRuntimeDependency(runtime.Id, lease.State.Version, lifetime, service);
            context.RegisterCleanup(lease, lifetime);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
