using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Library;

public static class CompositeRenderGraphExtensions
{
    public static DrawRenderPassResources AddComposite(
        this GpuRenderGraph graph,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        bool markOutput = true)
    {
        Validate(material);
        return graph.AddFullscreen(name, material, target, markOutput);
    }

    public static DrawRenderPassResources AddComposite(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        bool markOutput = true)
    {
        Validate(material);
        return context.AddFullscreen(name, material, target, markOutput);
    }

    public static DrawRenderPassResources AddComposite(
        this GpuRenderGraph graph,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        GpuRenderGraphTexture targetResource,
        bool markOutput = true)
    {
        Validate(material);
        return graph.AddFullscreen(name, material, target, targetResource, markOutput);
    }

    public static DrawRenderPassResources AddComposite(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        GpuRenderGraphTexture targetResource,
        bool markOutput = true)
    {
        Validate(material);
        return context.AddFullscreen(name, material, target, targetResource, markOutput);
    }

    private static void Validate(DrawMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.SampledTextures.Count < 2)
        {
            throw new ArgumentException("A composite material must declare at least two sampled textures.", nameof(material));
        }
    }
}
