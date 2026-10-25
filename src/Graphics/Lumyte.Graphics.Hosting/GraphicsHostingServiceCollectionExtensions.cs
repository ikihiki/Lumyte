using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Hosting;

/// <summary>Registers scoped graphics contexts.</summary>
public static class GraphicsHostingServiceCollectionExtensions
{
    /// <summary>Registers a named scoped service and constructor-injected factory.</summary>
    /// <typeparam name="TFactory">The backend factory type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The unique context name.</param>
    /// <returns>The same collection with the context registration added.</returns>
    public static IServiceCollection AddGraphicsDevice<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>(this IServiceCollection services, string name)
        where TFactory : class, IGraphicsDeviceFactory
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (services.Any(x => x.IsKeyedService && x.ServiceType == typeof(IGraphicsDeviceService) && Equals(x.ServiceKey, name)))
        {
            throw new ArgumentException("Graphics context name is already registered.", nameof(name));
        }

        services.AddKeyedScoped<TFactory>(name);
        services.AddKeyedScoped<IGraphicsDeviceService>(name, (provider, _) => new GraphicsDeviceService(name, provider.GetRequiredKeyedService<TFactory>(name)));
        return services;
    }
}
