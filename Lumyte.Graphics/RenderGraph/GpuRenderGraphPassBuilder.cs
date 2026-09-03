namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphPassBuilder
{
    private readonly GpuRenderGraph owner;
    private readonly GpuRenderGraphPassDeclaration pass;

    internal GpuRenderGraphPassBuilder(GpuRenderGraph owner, GpuRenderGraphPassDeclaration pass)
    {
        this.owner = owner;
        this.pass = pass;
    }

    public GpuRenderGraphPassBuilder Read(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.Read, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder Write(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.Write, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder ReadWrite(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.ReadWrite, stage, hazards);
        return this;
    }
}
