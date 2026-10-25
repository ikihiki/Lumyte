using System.Diagnostics.CodeAnalysis;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumyte.Resources.DependencyInjection;

/// <summary>Registers one shared resource manager and its application-lifetime source, stages and environments.</summary>
public static class ResourceServiceCollectionExtensions
{
    private static readonly Func<IServiceProvider, object> _runtimeModuleFactory =
        static provider => provider.GetRequiredService<ResourceRuntime>();

    /// <summary>Registers one singleton manager with immutable pipeline configuration.</summary>
    /// <param name="services">The target collection, including an <see cref="IMemoryManager"/> registration.</param>
    /// <param name="configure">The callback registering the shared source and all resource pipelines.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddResources(this IServiceCollection services, Action<ResourceRegistrationBuilder> configure)
    {
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(ResourceManager)))
        {
            throw new ArgumentException("Resource management is already registered.", nameof(services));
        }

        var builder = new ResourceRegistrationBuilder();
        configure(builder);
        Func<IServiceProvider, IResourceSource> source = builder.Complete();
        services.TryAddSingleton<ResourceRuntime>();
        services.TryAddSingleton<IResourceRuntime>(static provider => provider.GetRequiredService<ResourceRuntime>());
        if (!services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IExecutionModule)
            && descriptor.ImplementationFactory == _runtimeModuleFactory))
        {
            services.Add(ServiceDescriptor.Singleton(typeof(IExecutionModule), _runtimeModuleFactory));
        }

        services.TryAddSingleton<ExecutionRuntime>();
        services.AddSingleton(provider => new ResourceManager(
            "resources",
            source(provider),
            provider.GetServices<IResourceStage>(),
            provider.GetRequiredService<IMemoryManager>(),
            provider.GetRequiredService<IResourceRuntime>(),
            provider.GetServices<RuntimeDependencyRegistration>().Select(registration => registration.Resolve(provider)),
            builder.ResolveStageRuntimes(provider)));
        builder.Apply(services);
        return services;
    }

    /// <summary>Adds a singleton runtime dependency to the registered resource manager.</summary>
    /// <typeparam name="TDependency">The runtime adapter implementation.</typeparam>
    /// <param name="services">The registered resource services.</param>
    /// <param name="dependencyId">The unique runtime dependency identifier.</param>
    /// <param name="factory">The DI factory creating the application-lifetime adapter.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddResourceRuntimeDependency<TDependency>(this IServiceCollection services, string dependencyId, Func<IServiceProvider, TDependency> factory)
        where TDependency : class, IResourceRuntimeDependency
    {
        ValidateRuntimeRegistration(services, dependencyId);
        services.AddKeyedSingleton<TDependency>(dependencyId, (provider, _) => factory(provider));
        RegisterRuntimeResolver<TDependency>(services, dependencyId);
        return services;
    }

    internal static void AddRuntimeDependency<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDependency>(IServiceCollection services, string dependencyId)
        where TDependency : class, IResourceRuntimeDependency
    {
        ValidateRuntimeRegistration(services, dependencyId);
        services.AddKeyedSingleton<TDependency>(dependencyId);
        RegisterRuntimeResolver<TDependency>(services, dependencyId);
    }

    private static void ValidateRuntimeRegistration(IServiceCollection services, string dependencyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyId);
        if (!services.Any(static descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(ResourceManager)))
        {
            throw new ArgumentException("Register resource management before adding runtime dependencies.", nameof(services));
        }

        if (services.Any(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(RuntimeDependencyRegistration)
            && descriptor.ImplementationInstance is RuntimeDependencyRegistration registration
            && registration.Id == dependencyId))
        {
            throw new ArgumentException("The runtime dependency identifier is already registered.", nameof(dependencyId));
        }
    }

    private static void RegisterRuntimeResolver<TDependency>(IServiceCollection services, string dependencyId)
        where TDependency : class, IResourceRuntimeDependency
    {
        var registration = new RuntimeDependencyRegistration(dependencyId, provider => provider.GetRequiredKeyedService<TDependency>(dependencyId));
        services.AddSingleton(registration);
    }

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
