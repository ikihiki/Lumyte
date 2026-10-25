using Lumyte.Graphics.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics;

/// <summary>Registers named graphics execution environments for resource stages.</summary>
public static class ResourceGraphicsServiceCollectionExtensions
{
    /// <summary>Registers a singleton graphics environment borrowing an application-wide device service.</summary>
    /// <param name="services">The service collection containing the resource and graphics registrations.</param>
    /// <param name="deviceServiceName">The name of the singleton graphics device service in this DI root.</param>
    /// <param name="dependencyId">The unique runtime dependency identifier within the manager.</param>
    /// <returns>The same collection with the graphics bridge registered.</returns>
    public static IServiceCollection AddResourceGraphics(this IServiceCollection services, string deviceServiceName, string dependencyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyId);
        ServiceDescriptor? manager = services.LastOrDefault(x => !x.IsKeyedService && x.ServiceType == typeof(ResourceManager));
        if (manager?.Lifetime != ServiceLifetime.Singleton)
        {
            throw new ArgumentException("Register the singleton resource manager before its graphics environment.", nameof(services));
        }

        ServiceDescriptor? device = services.LastOrDefault(x => x.IsKeyedService && x.ServiceType == typeof(IGraphicsDeviceService) && Equals(x.ServiceKey, deviceServiceName));
        if (device is null)
        {
            throw new ArgumentException("Register the named graphics device service before its resource bridge.", nameof(deviceServiceName));
        }

        if (device.Lifetime != ServiceLifetime.Singleton)
        {
            throw new ArgumentException("A resource graphics environment requires a singleton device service.", nameof(deviceServiceName));
        }

        if (services.Any(x => x.IsKeyedService && x.ServiceType == typeof(GraphicsResourceRuntime) && Equals(x.ServiceKey, dependencyId)))
        {
            throw new ArgumentException("A graphics environment is already registered with this manager-local identifier.", nameof(dependencyId));
        }

        services.AddKeyedSingleton<GraphicsResourceRuntime>(dependencyId, (provider, _) => new GraphicsResourceRuntime(
            dependencyId,
            provider.GetRequiredKeyedService<IGraphicsDeviceService>(deviceServiceName),
            provider.GetRequiredService<IResourceRuntime>()));
        return services;
    }

    /// <summary>Resolves the manager-local graphics environment for a stage runtime binding.</summary>
    /// <param name="provider">A service provider from the same DI root as the resource manager.</param>
    /// <param name="dependencyId">The registered graphics runtime identifier.</param>
    /// <returns>The singleton runtime shared by stages bound to this environment.</returns>
    public static GraphicsResourceRuntime GetResourceGraphicsRuntime(this IServiceProvider provider, string dependencyId)
        => provider.GetRequiredKeyedService<GraphicsResourceRuntime>(dependencyId);
}
