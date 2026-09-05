namespace Lumyte.Graphics.RenderGraph;

/// <summary>Builds, submits, and presents one render graph.</summary>
public sealed class GpuFrame : IDisposable
{
    private readonly GpuRenderContext context;
    private readonly IGpuPresentationAdapter presentation;
    private bool completed;

    internal GpuFrame(
        GpuRenderContext context,
        IGpuPresentationAdapter presentation,
        GpuPresentationTarget target)
    {
        this.context = context;
        this.presentation = presentation;
        Target = target;
        Graph = new();
        TargetResource = Graph.ImportTexture("presentation-target", target.View.Texture, target.Description);
    }

    public GpuRenderGraph Graph { get; }
    public GpuPresentationTarget Target { get; }
    public GpuRenderGraphTexture TargetResource { get; }

    /// <summary>Compiles, submits, and presents this frame exactly once.</summary>
    public GpuRenderGraphExecution Submit()
    {
        if (completed) { throw new InvalidOperationException("The GPU frame has already ended."); }
        Graph.MarkOutput(TargetResource);
        GpuRenderGraphExecution execution = context.Submit(Graph);
        try { presentation.Present(Target, execution.Completion); }
        catch
        {
            execution.Dispose();
            completed = true;
            throw;
        }
        completed = true;
        return execution;
    }

    public void Dispose()
    {
        if (completed) { return; }
        presentation.Discard(Target);
        completed = true;
    }
}
