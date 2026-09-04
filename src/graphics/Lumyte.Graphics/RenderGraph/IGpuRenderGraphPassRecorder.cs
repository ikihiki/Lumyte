namespace Lumyte.Graphics.RenderGraph;

internal interface IGpuRenderGraphPassRecorder
{
    void Record(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources);
}
