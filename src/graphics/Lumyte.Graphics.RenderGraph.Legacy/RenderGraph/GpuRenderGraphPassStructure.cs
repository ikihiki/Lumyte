namespace Lumyte.Graphics.RenderGraph.Legacy;

internal sealed record GpuRenderGraphPassStructure(
    string Name,
    GpuRenderGraphPassFlags Flags,
    GpuRenderGraphAccessStructure[] Accesses);
