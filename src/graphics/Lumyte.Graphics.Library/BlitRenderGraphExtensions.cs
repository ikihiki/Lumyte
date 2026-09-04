using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Library;

public static class BlitRenderGraphExtensions
{
    public static DrawRenderPassResources AddBlit(
        this GpuRenderGraph graph,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        bool markOutput = true)
    {
        Validate(material);
        return graph.AddFullscreen(name, material, target, markOutput);
    }

    public static DrawRenderPassResources AddBlit(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        bool markOutput = true)
    {
        Validate(material);
        return context.AddFullscreen(name, material, target, markOutput);
    }

    public static DrawRenderPassResources AddBlit(
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

    public static DrawRenderPassResources AddBlit(
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
        if (material.SampledTextures.Count != 1)
        {
            throw new ArgumentException("A blit material must declare exactly one sampled texture.", nameof(material));
        }
    }
}
