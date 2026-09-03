namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphBarrierPlan
{
    internal GpuRenderGraphBarrierPlan(
        string destinationPass,
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards,
        GpuRenderGraphResource[] resources)
    {
        DestinationPass = destinationPass;
        Before = before;
        After = after;
        Hazards = hazards;
        Resources = Array.AsReadOnly(resources);
    }

    public string DestinationPass { get; }
    public GpuStage Before { get; }
    public GpuStage After { get; }
    public GpuBarrierHazards Hazards { get; }
    public IReadOnlyList<GpuRenderGraphResource> Resources { get; }
}
