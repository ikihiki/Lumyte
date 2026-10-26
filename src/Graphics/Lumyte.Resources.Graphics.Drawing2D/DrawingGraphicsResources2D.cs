using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Exposes keyed shared Graphics resource definitions used by pure and layered 2D drawing.</summary>
public static class DrawingGraphicsResources2D
{
    /// <summary>Gets the drawing vertex module key.</summary>
    public static ResourceKey<GraphicsResource<IGraphicsShader>> VertexShader { get; } = new("Lumyte/Drawing2D/vertex");

    /// <summary>Gets the paint/coverage fragment module key.</summary>
    public static ResourceKey<GraphicsResource<IGraphicsShader>> FragmentShader { get; } = new("Lumyte/Drawing2D/fragment");

    /// <summary>Gets the shared graphics pipeline key.</summary>
    public static ResourceKey<GraphicsResource<IGraphicsPipeline>> Pipeline { get; } = new("Lumyte/Drawing2D/pipeline");

    /// <summary>Gets the shared nearest clamp sampler key.</summary>
    public static ResourceKey<GraphicsResource<IGraphicsSampler>> Sampler { get; } = new("Lumyte/Drawing2D/sampler");

    internal static void RegisterDefaults(IServiceCollection services)
    {
        services.TryAddGraphicsResource<IGraphicsShader>(VertexShader, provider => provider.GetService<GraphicsShaderPackage<Renderer2DResource>>() is { } package
            ? new GraphicsShaderDefinition(package.Vertex) : new GraphicsShaderDefinition(static () => DrawingShaders2D.Vertex));
        services.TryAddGraphicsResource<IGraphicsShader>(FragmentShader, provider => provider.GetService<GraphicsShaderPackage<Renderer2DResource>>() is { } package
            ? new GraphicsShaderDefinition(package.Fragment) : new GraphicsShaderDefinition(static () => DrawingShaders2D.Fragment));
        services.TryAddGraphicsResource<IGraphicsPipeline>(Pipeline, static _ => new GraphicsPipelineDefinition { VertexShader = VertexShader, FragmentShader = FragmentShader, Label = "drawing-2d" });
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
