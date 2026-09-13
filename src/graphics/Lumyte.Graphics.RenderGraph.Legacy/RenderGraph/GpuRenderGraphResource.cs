namespace Lumyte.Graphics.RenderGraph.Legacy;

internal readonly record struct GpuRenderGraphResource(int Value)
{
    public bool IsNull => Value == 0;
}
