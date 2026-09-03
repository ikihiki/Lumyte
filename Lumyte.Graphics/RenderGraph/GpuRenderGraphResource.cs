namespace Lumyte.Graphics.RenderGraph;

public readonly record struct GpuRenderGraphResource(int Value)
{
    public bool IsNull => Value == 0;
}
