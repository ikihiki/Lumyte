using Lumyte.Graphics.Abstractions;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumyte.Resources.Graphics;

/// <summary>Registers keyed definitions once, independently of rendering calls.</summary>
public static class GraphicsResourceDefinitionExtensions
{
    /// <summary>Registers a definition; the last registration for this exact typed key wins.</summary>
    /// <typeparam name="T">The owned native contract.</typeparam>
    /// <param name="services">The application's services.</param>
    /// <param name="key">The resource key used for acquisition.</param>
    /// <param name="definition">The immutable reconstruction recipe.</param>
    /// <returns>The supplied services.</returns>
    public static IServiceCollection AddGraphicsResource<T>(this IServiceCollection services, ResourceKey<GraphicsResource<T>> key, GraphicsResourceDefinition<T> definition)
        where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(definition);
        services.AddKeyedSingleton<GraphicsResourceDefinition<T>>(key, definition);
        return AddRegistration(services, key);
    }

    /// <summary>Registers a library default without replacing an application definition.</summary>
    /// <typeparam name="T">The owned native contract.</typeparam>
    /// <param name="services">The application's services.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="createDefinition">Resolves configuration once when the manager is constructed.</param>
    /// <returns>The supplied services.</returns>
    public static IServiceCollection TryAddGraphicsResource<T>(this IServiceCollection services, ResourceKey<GraphicsResource<T>> key, Func<IServiceProvider, GraphicsResourceDefinition<T>> createDefinition)
        where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(createDefinition);
        services.TryAddKeyedSingleton<GraphicsResourceDefinition<T>>(key, (provider, _) => createDefinition(provider));
        return AddRegistration(services, key);
    }

    /// <summary>Adds the keyed resource stage for a native contract; repeated calls preserve the existing pipeline.</summary>
    /// <typeparam name="T">The owned native contract, including custom composite assets or typed buffers.</typeparam>
    /// <param name="builder">The resource pipeline configuration.</param>
    /// <returns>The supplied builder.</returns>
    public static ResourceRegistrationBuilder AddGraphicsResourcePipeline<T>(this ResourceRegistrationBuilder builder)
        where T : class, IDisposable
        => builder.TryAddSourcePipeline<GraphicsResource<T>, GraphicsResourceStage<T>, GraphicsResourceRuntime>();

    /// <summary>Adds the standard shared shader, graphics/compute pipeline, sampler, texture and view resource stages.</summary>
    /// <param name="builder">The resource pipeline configuration.</param>
    /// <returns>The supplied builder.</returns>
    public static ResourceRegistrationBuilder AddGraphicsResources(this ResourceRegistrationBuilder builder)
        => builder.AddGraphicsResourcePipeline<IGraphicsShader>()
            .AddGraphicsResourcePipeline<IGraphicsPipeline>()
            .AddGraphicsResourcePipeline<IGraphicsComputePipeline>()
            .AddGraphicsResourcePipeline<IGraphicsSampler>()
            .AddGraphicsResourcePipeline<IGraphicsTexture>()
            .AddGraphicsResourcePipeline<IGraphicsTextureView>();

    private static IServiceCollection AddRegistration<T>(IServiceCollection services, ResourceKey<GraphicsResource<T>> key)
        where T : class, IDisposable
    {
        services.AddSingleton(provider => new GraphicsResourceRegistration<T>(key, provider.GetRequiredKeyedService<GraphicsResourceDefinition<T>>(key)));
        return services;
    }
}
