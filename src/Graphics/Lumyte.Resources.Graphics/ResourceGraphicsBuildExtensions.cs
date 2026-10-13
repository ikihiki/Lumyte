using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics;

/// <summary>Records the exact graphics epoch used by a resource stage.</summary>
public static class ResourceGraphicsBuildExtensions
{
    /// <summary>Acquires a graphics epoch and transfers its lease to the build context.</summary>
    /// <param name="context">The current resource build.</param>
    /// <param name="service">The same scoped service registered for the runtime dependency.</param>
    /// <param name="dependencyId">The registered graphics runtime dependency identifier.</param>
    /// <param name="lifetime">Whether the physical device is needed until build completion or resource release.</param>
    /// <param name="cancellationToken">Cancels before a graphics lease is acquired.</param>
    /// <returns>The borrowed lease retaining the exact device epoch; the caller must not dispose it.</returns>
    /// <remarks>The stage releases native children before context cleanup returns the lease. Device retirement and thread dispatch remain the stage's responsibility.</remarks>
    public static async ValueTask<GraphicsDeviceLease> UseGraphicsAsync(this ResourceBuildContext context, IGraphicsDeviceService service, string dependencyId, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyId);
        GraphicsDeviceLease lease = await service.AcquireAsync(cancellationToken);
        try
        {
            context.TrackRuntimeDependency(dependencyId, lease.State.Version, lifetime, service);
            context.RegisterCleanup(
                () =>
                {
                    lease.Dispose();
                    return ValueTask.CompletedTask;
                },
                lifetime);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
