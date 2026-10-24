using Lumyte.Graphics.Hosting;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics;

/// <summary>Registers named graphics execution environments for resource stages.</summary>
public static class ResourceGraphicsServiceCollectionExtensions
{
    /// <summary>Registers a scoped graphics runtime and its generation provider without initializing a device.</summary>
    /// <param name="services">The service collection containing the resource and graphics registrations.</param>
    /// <param name="resourceManagerName">The name of the resource manager.</param>
    /// <param name="deviceServiceName">The name of the graphics device service in the same DI scope.</param>
    /// <param name="dependencyId">The unique runtime dependency identifier within the manager.</param>
    /// <returns>The same collection with the graphics bridge registered.</returns>
    public static IServiceCollection AddResourceGraphics(this IServiceCollection services, string resourceManagerName, string deviceServiceName, string dependencyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceServiceName);
        if (!services.Any(x => x.IsKeyedService && x.ServiceType == typeof(IGraphicsDeviceService) && Equals(x.ServiceKey, deviceServiceName)))
        {
            throw new ArgumentException("Register the named graphics device service before its resource bridge.", nameof(deviceServiceName));
        }

        var key = new RuntimeKey(resourceManagerName, dependencyId);
        services.AddResourceRuntimeDependency(
            resourceManagerName,
            dependencyId,
            provider => provider.GetRequiredKeyedService<GraphicsResourceRuntime>(key));
        services.AddKeyedScoped<GraphicsResourceRuntime>(key, (provider, _) => new GraphicsResourceRuntime(
            dependencyId,
            provider.GetRequiredKeyedService<IGraphicsDeviceService>(deviceServiceName),
            provider.GetRequiredService<IResourceRuntime>()));
        return services;
    }

    /// <summary>Resolves the manager-local graphics environment for a stage runtime binding.</summary>
    /// <param name="provider">The same scope used to construct the resource manager.</param>
    /// <param name="resourceManagerName">The registered resource manager name.</param>
    /// <param name="dependencyId">The registered graphics runtime identifier.</param>
    /// <returns>The scoped runtime shared by stages bound to this environment.</returns>
    public static GraphicsResourceRuntime GetResourceGraphicsRuntime(this IServiceProvider provider, string resourceManagerName, string dependencyId)
        => provider.GetRequiredKeyedService<GraphicsResourceRuntime>(new RuntimeKey(resourceManagerName, dependencyId));

    private sealed record RuntimeKey(string ManagerName, string Id);
}
