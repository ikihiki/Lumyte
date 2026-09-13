namespace Lumyte.Graphics.RenderGraph.Legacy;

internal interface IGpuRenderGraphPassRecorder
{
    void Record(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources);
}
