namespace Lumyte.Graphics.RenderGraph;

internal readonly record struct GpuRenderGraphResource(int Value)
{
    public bool IsNull => Value == 0;
}
