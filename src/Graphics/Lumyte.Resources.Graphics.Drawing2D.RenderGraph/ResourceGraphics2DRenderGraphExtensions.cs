using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;

namespace Lumyte.Resources.Graphics.Drawing2D.RenderGraph;

/// <summary>Creates layered graph renderers from the same preloaded resource used by pure drawing.</summary>
public static class ResourceGraphics2DRenderGraphExtensions
{
    /// <summary>Creates a layered renderer retaining a single configured shader generation.</summary>
    /// <param name="reference">The factory reference loaded with AcquireRenderer2DAsync.</param>
    /// <param name="renderers">The borrowed drawing strategies.</param>
    /// <param name="list">The layered recording.</param>
    /// <param name="target">The graph-local destination.</param>
    /// <param name="bindings">The receiving graph's external resource bindings.</param>
    /// <param name="snapshot">An optional publication shared with other inputs and effects.</param>
    /// <returns>A renderer owning its program protection; prepared passes retain their own protection.</returns>
    /// <remarks>Configure effects on the parent graph with AddImageEffects(manager), using the same UseGraphicsShaders API.</remarks>
    public static RenderGraphRenderer2D CreateRenderGraphRenderer(
        this ResourceReference<Renderer2DResource> reference,
        DrawingCommandRenderers2D renderers,
        RenderGraphDrawList2D list,
        RenderTargetBinding2D target,
        DrawingGraphBindings2D bindings,
        ResourceSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ResourceLease<Renderer2DResource> lease = reference.Acquire(snapshot);
        try
        {
            var programs = new RetainedGraphicsProgramProvider<Renderer2DResource>(lease, static resource => resource.Program, DrawingPrograms2D.ProgramId);
            return new RenderGraphRenderer2D(renderers, list, target, bindings, programs, ownsPrograms: true);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
