using Lumyte.Graphics.Hosting;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics;

/// <summary>Connects named resource managers to their scoped graphics device services.</summary>
public static class ResourceGraphicsServiceCollectionExtensions
{
    /// <summary>Registers a graphics runtime dependency without initializing a device.</summary>
    /// <param name="services">The service collection containing the resource and graphics registrations.</param>
    /// <param name="resourceManagerName">The name of the resource manager.</param>
    /// <param name="deviceServiceName">The name of the graphics device service in the same DI scope.</param>
    /// <param name="dependencyId">The unique runtime dependency identifier within the manager.</param>
    /// <returns>The same collection with the graphics bridge registered.</returns>
    public static IServiceCollection AddResourceGraphics(this IServiceCollection services, string resourceManagerName, string deviceServiceName, string dependencyId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceServiceName);
        if (!services.Any(x => x.IsKeyedService && x.ServiceType == typeof(IGraphicsDeviceService) && Equals(x.ServiceKey, deviceServiceName)))
        {
            throw new ArgumentException("Register the named graphics device service before its resource bridge.", nameof(deviceServiceName));
        }

        return services.AddResourceRuntimeDependency(
            resourceManagerName,
            dependencyId,
            provider => new GraphicsResourceDependency(dependencyId, provider.GetRequiredKeyedService<IGraphicsDeviceService>(deviceServiceName)));
    }
}
