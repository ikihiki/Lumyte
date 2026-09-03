namespace Lumyte.Graphics.RenderGraph;

internal sealed class GpuRenderGraphStructure(
    ulong hash,
    GpuRenderGraphResourceStructure[] resources,
    GpuRenderGraphPassStructure[] passes)
{
    public ulong Hash { get; } = hash;
    public GpuRenderGraphResourceStructure[] Resources { get; } = resources;
    public GpuRenderGraphPassStructure[] Passes { get; } = passes;
}
