namespace Lumyte.Graphics.RenderGraph;

internal sealed class GpuRenderGraphNoopPassRecorder : IGpuRenderGraphPassRecorder
{
    public static GpuRenderGraphNoopPassRecorder Instance { get; } = new();

    private GpuRenderGraphNoopPassRecorder() { }

    public void Record(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources) { }
}
