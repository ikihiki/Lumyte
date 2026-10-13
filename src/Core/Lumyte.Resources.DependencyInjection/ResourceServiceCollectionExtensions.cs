using Lumyte.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.DependencyInjection;

/// <summary>Registers named resource managers and their scoped source, stage and runtime services.</summary>
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
            provider.GetKeyedServices<ResourcePipeline>(name),
            provider.GetRequiredService<IMemoryManager>(),
            provider.GetKeyedServices<RuntimeDependencyRegistration>(name).Select(registration => registration.Resolve(provider))));
        builder.Apply(services);
        return services;
    }

    /// <summary>Adds a scoped runtime adapter to an already registered named resource manager.</summary>
    /// <typeparam name="TDependency">The runtime adapter implementation.</typeparam>
    /// <param name="services">The target service collection.</param>
    /// <param name="managerName">The registered resource manager name.</param>
    /// <param name="dependencyId">The unique manager-local runtime dependency name.</param>
    /// <param name="factory">The DI factory creating the owned scoped adapter.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddResourceRuntimeDependency<TDependency>(this IServiceCollection services, string managerName, string dependencyId, Func<IServiceProvider, TDependency> factory)
        where TDependency : class, IResourceRuntimeDependency
    {
        ArgumentNullException.ThrowIfNull(factory);
        ValidateRuntimeRegistration(services, managerName, dependencyId);
        var key = new RuntimeServiceKey(managerName, dependencyId);
        services.AddKeyedScoped<TDependency>(key, (provider, _) => factory(provider));
        RegisterRuntimeResolver<TDependency>(services, managerName, dependencyId, key);
        return services;
    }

    internal static void AddRuntimeDependency<TDependency>(IServiceCollection services, string managerName, string dependencyId)
        where TDependency : class, IResourceRuntimeDependency
    {
        ValidateRuntimeRegistration(services, managerName, dependencyId);
        var key = new RuntimeServiceKey(managerName, dependencyId);
        services.AddKeyedScoped<TDependency>(key);
        RegisterRuntimeResolver<TDependency>(services, managerName, dependencyId, key);
    }

    private static bool HasManager(IServiceCollection services, string name)
        => services.Any(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(ResourceManager) && Equals(descriptor.ServiceKey, name));

    private static void ValidateRuntimeRegistration(IServiceCollection services, string managerName, string dependencyId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(managerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyId);
        if (!HasManager(services, managerName))
        {
            throw new ArgumentException("Register the resource manager before adding runtime dependencies.", nameof(managerName));
        }

        if (services.Any(descriptor => descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(RuntimeDependencyRegistration)
            && Equals(descriptor.ServiceKey, managerName)
            && descriptor.KeyedImplementationInstance is RuntimeDependencyRegistration registration
            && registration.Id == dependencyId))
        {
            throw new ArgumentException("The runtime dependency name is already registered for this resource manager.", nameof(dependencyId));
        }
    }

    private static void RegisterRuntimeResolver<TDependency>(IServiceCollection services, string managerName, string dependencyId, RuntimeServiceKey key)
        where TDependency : class, IResourceRuntimeDependency
    {
        var registration = new RuntimeDependencyRegistration(dependencyId, provider => provider.GetRequiredKeyedService<TDependency>(key));
        services.AddKeyedSingleton(managerName, registration);
    }

    private sealed record RuntimeServiceKey(string ManagerName, string Id);

    private sealed class RuntimeDependencyRegistration(string id, Func<IServiceProvider, IResourceRuntimeDependency> resolve)
    {
        internal string Id { get; } = id;

        internal IResourceRuntimeDependency Resolve(IServiceProvider provider)
        {
            IResourceRuntimeDependency dependency = resolve(provider);
            if (!string.Equals(dependency.Id, Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Runtime dependency '{Id}' returned the mismatched identifier '{dependency.Id}'.");
            }

            return dependency;
        }
    }
}
