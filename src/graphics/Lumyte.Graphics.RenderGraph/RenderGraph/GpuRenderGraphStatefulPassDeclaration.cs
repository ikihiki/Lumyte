namespace Lumyte.Graphics.RenderGraph;

internal sealed class GpuRenderGraphStatefulPassDeclaration<TState>(
    string name,
    TState state,
    GpuRenderGraphPassAction<TState> record,
    GpuRenderGraphPassFlags flags) : GpuRenderGraphPassDeclaration(name, flags)
{
    public override void Record(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources)
        => record(new(commands, backend, resources, allowedResources), state);
}
