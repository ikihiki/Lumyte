using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Hosting;

/// <summary>Registers default or named graphics contexts with scoped or application ownership.</summary>
public static class GraphicsHostingServiceCollectionExtensions
{
    /// <summary>Registers the default device service and constructor-injected factory with the same lifetime.</summary>
    /// <typeparam name="TFactory">The backend factory type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Scoped by default; use singleton for an application-wide device.</param>
    /// <returns>The same collection with the default context registration added.</returns>
    public static IServiceCollection AddGraphicsDevice<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TFactory : class, IGraphicsDeviceFactory
    {
        if (lifetime is not (ServiceLifetime.Scoped or ServiceLifetime.Singleton))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Graphics device ownership must be scoped or singleton.");
        }

        if (services.Any(x => !x.IsKeyedService && x.ServiceType == typeof(IGraphicsDeviceService)))
        {
            throw new ArgumentException("The default graphics context is already registered.", nameof(services));
        }

        services.Add(new ServiceDescriptor(typeof(TFactory), typeof(TFactory), lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IGraphicsDeviceService),
            static provider => new GraphicsDeviceService(string.Empty, provider.GetRequiredService<TFactory>()),
            lifetime));
        return services;
    }

    /// <summary>Registers a named device service and constructor-injected factory with the same lifetime.</summary>
    /// <typeparam name="TFactory">The backend factory type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The unique context name.</param>
    /// <param name="lifetime">Scoped by default; use singleton for an application-wide device.</param>
    /// <returns>The same collection with the context registration added.</returns>
    public static IServiceCollection AddGraphicsDevice<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>(this IServiceCollection services, string name, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TFactory : class, IGraphicsDeviceFactory
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (lifetime is not (ServiceLifetime.Scoped or ServiceLifetime.Singleton))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Graphics device ownership must be scoped or singleton.");
        }

        if (services.Any(x => x.IsKeyedService && x.ServiceType == typeof(IGraphicsDeviceService) && Equals(x.ServiceKey, name)))
        {
            throw new ArgumentException("Graphics context name is already registered.", nameof(name));
        }

        services.Add(new ServiceDescriptor(typeof(TFactory), name, typeof(TFactory), lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IGraphicsDeviceService),
            name,
            (provider, _) => new GraphicsDeviceService(name, provider.GetRequiredKeyedService<TFactory>(name)),
            lifetime));
        return services;
    }
}
