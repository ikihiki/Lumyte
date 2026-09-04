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
        GpuRenderGraphTexture texture,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, texture.Resource, GpuRenderGraphAccess.Read, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder Read(
        GpuRenderGraphBuffer buffer,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, buffer.Resource, GpuRenderGraphAccess.Read, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder Read(GpuRenderGraphDependency dependency)
    {
        owner.AddAccess(
            pass,
            dependency.Resource,
            GpuRenderGraphAccess.Read,
            GpuStage.None,
            GpuBarrierHazards.None);
        return this;
    }

    public GpuRenderGraphPassBuilder Write(
        GpuRenderGraphTexture texture,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, texture.Resource, GpuRenderGraphAccess.Write, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder Write(
        GpuRenderGraphBuffer buffer,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, buffer.Resource, GpuRenderGraphAccess.Write, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder Write(GpuRenderGraphDependency dependency)
    {
        owner.AddAccess(
            pass,
            dependency.Resource,
            GpuRenderGraphAccess.Write,
            GpuStage.None,
            GpuBarrierHazards.None);
        return this;
    }

    public GpuRenderGraphPassBuilder ReadWrite(
        GpuRenderGraphTexture texture,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, texture.Resource, GpuRenderGraphAccess.ReadWrite, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder ReadWrite(
        GpuRenderGraphBuffer buffer,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, buffer.Resource, GpuRenderGraphAccess.ReadWrite, stage, hazards);
        return this;
    }

    /// <summary>Declares graph resources referenced by bindless shader-array indices.</summary>
    public GpuRenderGraphPassBuilder UseShaderBindings(GpuRenderGraphShaderBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        bindings.DeclareOn(this);
        return this;
    }
}
