using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Library;

public static class FullscreenRenderGraphExtensions
{
    public static DrawRenderPassResources AddFullscreen(
        this GpuRenderGraph graph,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        bool markOutput = true)
        => graph.AddDraw(name, CreateDraw(material), target, markOutput);

    public static DrawRenderPassResources AddFullscreen(
        this GpuRenderGraph graph,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        GpuRenderGraphTexture targetResource,
        bool markOutput = true)
        => graph.AddDraw(name, CreateDraw(material), target, targetResource, markOutput);

    public static DrawRenderPassResources AddFullscreen(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        bool markOutput = true)
        => context.AddDraw(name, CreateDraw(material), target, markOutput);

    public static DrawRenderPassResources AddFullscreen(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawMaterial material,
        DrawRenderTarget target,
        GpuRenderGraphTexture targetResource,
        bool markOutput = true)
        => context.AddDraw(name, CreateDraw(material), target, targetResource, markOutput);

    private static DrawData CreateDraw(DrawMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return new(material, new(3), DrawTransforms.Identity);
    }
}
