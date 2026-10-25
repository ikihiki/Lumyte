using Lumyte.Graphics.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumyte.Resources.Graphics;

/// <summary>Registers graphics execution environments selected by type or device name.</summary>
public static class ResourceGraphicsServiceCollectionExtensions
{
    /// <summary>Registers a singleton graphics environment; the first registration supplies the default environment.</summary>
    /// <param name="services">The service collection containing the resource and graphics registrations.</param>
    /// <param name="deviceServiceName">The name of the singleton graphics device service in this DI root.</param>
    /// <returns>The same collection with the graphics bridge registered.</returns>
    public static IServiceCollection AddResourceGraphics(this IServiceCollection services, string deviceServiceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceServiceName);
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

        if (services.Any(x => x.IsKeyedService && x.ServiceType == typeof(GraphicsResourceRuntime) && Equals(x.ServiceKey, deviceServiceName)))
        {
            throw new ArgumentException("A resource graphics environment is already registered for this device service.", nameof(deviceServiceName));
        }

        services.AddKeyedSingleton<GraphicsResourceRuntime>(deviceServiceName, (provider, _) => new GraphicsResourceRuntime(
            provider.GetRequiredKeyedService<IGraphicsDeviceService>(deviceServiceName),
            provider.GetRequiredService<IResourceRuntime>()));
        services.TryAddSingleton(provider => provider.GetRequiredKeyedService<GraphicsResourceRuntime>(deviceServiceName));
        return services;
    }

    /// <summary>Resolves the graphics environment borrowing the named device service.</summary>
    /// <param name="provider">A service provider from the same DI root as the resource manager.</param>
    /// <param name="deviceServiceName">The name of the registered graphics device service.</param>
    /// <returns>The singleton runtime shared by stages bound to this environment.</returns>
    public static GraphicsResourceRuntime GetResourceGraphicsRuntime(this IServiceProvider provider, string deviceServiceName)
        => provider.GetRequiredKeyedService<GraphicsResourceRuntime>(deviceServiceName);
}
