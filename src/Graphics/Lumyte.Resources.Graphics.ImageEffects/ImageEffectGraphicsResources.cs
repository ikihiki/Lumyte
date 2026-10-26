using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Exposes the keyed shared resources used by the library's image-effect passes.</summary>
public static class ImageEffectGraphicsResources
{
    /// <summary>Gets the fullscreen vertex module key.</summary>
    public static ResourceKey<GraphicsResource<IGraphicsShader>> VertexShader { get; } = new("Lumyte/ImageEffects/vertex");

    /// <summary>Gets the image-effect fragment module key.</summary>
    public static ResourceKey<GraphicsResource<IGraphicsShader>> FragmentShader { get; } = new("Lumyte/ImageEffects/fragment");

    /// <summary>Gets the shared graphics pipeline key.</summary>
    public static ResourceKey<GraphicsResource<IGraphicsPipeline>> Pipeline { get; } = new("Lumyte/ImageEffects/pipeline");

    /// <summary>Gets the nearest clamp sampler key.</summary>
    public static ResourceKey<GraphicsResource<IGraphicsSampler>> Sampler { get; } = new("Lumyte/ImageEffects/sampler");

    internal static void RegisterDefaults(IServiceCollection services)
    {
        services.TryAddGraphicsResource<IGraphicsShader>(VertexShader, provider => provider.GetService<GraphicsShaderPackage<ImageEffectShaderResource>>() is { } package
            ? new GraphicsShaderDefinition(package.Vertex) : new GraphicsShaderDefinition(static () => ImageEffectShaders.Embedded.Fullscreen));
        services.TryAddGraphicsResource<IGraphicsShader>(FragmentShader, provider => provider.GetService<GraphicsShaderPackage<ImageEffectShaderResource>>() is { } package
            ? new GraphicsShaderDefinition(package.Fragment) : new GraphicsShaderDefinition(static () => ImageEffectShaders.Embedded.Effects));
        services.TryAddGraphicsResource<IGraphicsPipeline>(Pipeline, static _ => new GraphicsPipelineDefinition { VertexShader = VertexShader, FragmentShader = FragmentShader, Label = "image-effect" });
        services.TryAddGraphicsResource<IGraphicsSampler>(Sampler, static _ => new GraphicsSamplerDefinition(new()
        {
            MinFilter = FilterMode.Nearest,
            MagFilter = FilterMode.Nearest,
            MipmapFilter = FilterMode.Nearest,
            AddressU = AddressMode.ClampToEdge,
            AddressV = AddressMode.ClampToEdge,
            LodMaxClamp = 0,
        }));
    }
}
