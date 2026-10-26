using Lumyte.Graphics.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics;

/// <summary>Configures shader inputs once at application composition, independently of rendering calls.</summary>
public static class GraphicsShaderServiceCollectionExtensions
{
    /// <summary>Registers shader inputs for a typed consumer; the last registration wins.</summary>
    /// <typeparam name="TConsumer">The resource type whose shader ABI the artifacts implement.</typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="vertex">The tracked vertex artifact key.</param>
    /// <param name="fragment">The tracked fragment artifact key.</param>
    /// <returns>The supplied collection.</returns>
    /// <remarks>Register the artifact pipeline separately. Omit this call to use consumer-provided embedded defaults.</remarks>
    public static IServiceCollection UseGraphicsShaders<TConsumer>(this IServiceCollection services, ResourceKey<ShaderArtifact> vertex, ResourceKey<ShaderArtifact> fragment)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new GraphicsShaderPackage<TConsumer>(vertex, fragment));
        return services;
    }
}
