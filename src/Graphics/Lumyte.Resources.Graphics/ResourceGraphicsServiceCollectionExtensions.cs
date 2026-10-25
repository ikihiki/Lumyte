using Lumyte.Graphics.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics;

/// <summary>Registers graphics execution environments selected by type or device name.</summary>
public static class ResourceGraphicsServiceCollectionExtensions
{
    /// <summary>Registers the default singleton graphics environment using the unnamed device service.</summary>
    /// <param name="services">The service collection containing the resource and graphics registrations.</param>
    /// <returns>The same collection with the graphics bridge registered.</returns>
    public static IServiceCollection AddResourceGraphics(this IServiceCollection services)
        => Register(services, null);

    /// <summary>Registers a named singleton graphics environment without changing the default environment.</summary>
    /// <param name="services">The service collection containing the resource and graphics registrations.</param>
    /// <param name="deviceServiceName">The name of the singleton graphics device service in this DI root.</param>
    /// <returns>The same collection with the graphics bridge registered.</returns>
    public static IServiceCollection AddResourceGraphics(this IServiceCollection services, string deviceServiceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceServiceName);
        return Register(services, deviceServiceName);
    }

    /// <summary>Resolves the graphics environment borrowing the named device service.</summary>
    /// <param name="provider">A service provider from the same DI root as the resource manager.</param>
    /// <param name="deviceServiceName">The name of the registered graphics device service.</param>
    /// <returns>The singleton runtime shared by stages bound to this environment.</returns>
    public static GraphicsResourceRuntime GetResourceGraphicsRuntime(this IServiceProvider provider, string deviceServiceName)
        => provider.GetRequiredKeyedService<GraphicsResourceRuntime>(deviceServiceName);

    private static IServiceCollection Register(IServiceCollection services, string? deviceServiceName)
    {
        ServiceDescriptor? manager = services.LastOrDefault(x => !x.IsKeyedService && x.ServiceType == typeof(ResourceManager));
        if (manager?.Lifetime != ServiceLifetime.Singleton)
        {
            throw new ArgumentException("Register the singleton resource manager before its graphics environment.", nameof(services));
        }

        ServiceDescriptor? device = services.LastOrDefault(x => x.ServiceType == typeof(IGraphicsDeviceService) &&
            (deviceServiceName is null ? !x.IsKeyedService : x.IsKeyedService && Equals(x.ServiceKey, deviceServiceName)));
        string parameterName = deviceServiceName is null ? nameof(services) : nameof(deviceServiceName);
        if (device is null)
        {
            throw new ArgumentException("Register the selected graphics device service before its resource bridge.", parameterName);
        }

        if (device.Lifetime != ServiceLifetime.Singleton)
        {
            throw new ArgumentException("A resource graphics environment requires a singleton device service.", parameterName);
        }

        if (services.Any(x => x.ServiceType == typeof(GraphicsResourceRuntime) &&
            (deviceServiceName is null ? !x.IsKeyedService : x.IsKeyedService && Equals(x.ServiceKey, deviceServiceName))))
        {
            throw new ArgumentException("A resource graphics environment is already registered for this device service.", parameterName);
        }

        if (deviceServiceName is null)
        {
            services.AddSingleton(static provider => new GraphicsResourceRuntime(
                provider.GetRequiredService<IGraphicsDeviceService>(),
                provider.GetRequiredService<IResourceRuntime>()));
        }
        else
        {
            services.AddKeyedSingleton<GraphicsResourceRuntime>(deviceServiceName, (provider, _) => new GraphicsResourceRuntime(
                provider.GetRequiredKeyedService<IGraphicsDeviceService>(deviceServiceName),
                provider.GetRequiredService<IResourceRuntime>()));
        }

        return services;
    }
}
