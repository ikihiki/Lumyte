namespace Lumyte.Graphics.RenderGraph;

internal sealed record GpuRenderGraphPassStructure(
    string Name,
    GpuRenderGraphPassFlags Flags,
    GpuRenderGraphAccessStructure[] Accesses);
