namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphAliasBarrierPlan
{
    internal GpuRenderGraphAliasBarrierPlan(
        string destinationPass,
        int reuseSlot,
        GpuRenderGraphResource beforeResource,
        GpuRenderGraphResource afterResource,
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards)
    {
        DestinationPass = destinationPass;
        ReuseSlot = reuseSlot;
        BeforeResource = beforeResource;
        AfterResource = afterResource;
        Before = before;
        After = after;
        Hazards = hazards;
    }

    public string DestinationPass { get; }
    public int ReuseSlot { get; }
    internal GpuRenderGraphResource BeforeResource { get; }
    internal GpuRenderGraphResource AfterResource { get; }
    public GpuStage Before { get; }
    public GpuStage After { get; }
    public GpuBarrierHazards Hazards { get; }
}
