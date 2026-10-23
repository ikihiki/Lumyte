using Lumyte.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.DependencyInjection;

/// <summary>Registers named resource managers and their scoped source and stage services.</summary>
public static class ResourceServiceCollectionExtensions
{
    /// <summary>Registers a named keyed scoped manager with immutable pipeline configuration.</summary>
    /// <param name="services">The target service collection, including an <see cref="IMemoryManager"/> registration.</param>
    /// <param name="name">The unique manager name.</param>
    /// <param name="configure">The callback registering source and pipeline types.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddResources(this IServiceCollection services, string name, Action<ResourceRegistrationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        if (HasManager(services, name))
        {
            throw new ArgumentException("The resource manager name is already registered.", nameof(name));
        }

        var builder = new ResourceRegistrationBuilder(name);
        configure(builder);
        Func<IServiceProvider, IResourceSource> source = builder.Complete();
        services.AddKeyedScoped<ResourceManager>(name, (provider, _) => new ResourceManager(
            name,
            source(provider),
            provider.GetKeyedServices<IResourceStage>(name),
            provider.GetRequiredService<IMemoryManager>()));
        builder.Apply(services);
        return services;
    }

    private static bool HasManager(IServiceCollection services, string name)
        => services.Any(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(ResourceManager) && Equals(descriptor.ServiceKey, name));
}
