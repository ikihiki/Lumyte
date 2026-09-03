namespace Lumyte.Graphics.RenderGraph;

internal abstract class GpuRenderGraphPassDeclaration(
    string name,
    GpuRenderGraphPassFlags flags) : IGpuRenderGraphPassRecorder
{
    public string Name { get; } = name;
    public GpuRenderGraphPassFlags Flags { get; } = flags;
    public List<GpuRenderGraphResourceAccess> Accesses { get; } = [];

    public abstract void Record(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources);
}
