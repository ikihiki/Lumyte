using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Appends explicitly completion-owned resident frames to a parent graph.</summary>
public static partial class Drawing2DRenderGraphExtensions
{
    /// <summary>Appends one frozen, completion-owned resident frame to a shared parent graph.</summary>
    /// <remarks>The caller retains frame, target and sampled resources through actual GPU completion,
    /// command disposal and plan disposal, then calls frame.Retire(). Attach the actual submission
    /// immediately after Submit. A culled or unrecorded frame is disposed without publishing residency;
    /// recorded but never submitted commands require AbortUnsubmitted after command disposal.
    /// Unknown submission acceptance must remain quarantined. This pass includes its initialization
    /// producer, so graph culling cannot separate initialization from its consuming raster work.
    /// The target must bind the exact external allocation used during frame preparation.</remarks>
    /// <param name="graph">The parent graph.</param>
    /// <param name="frame">The caller-owned prepared frame, appended and recorded at most once.</param>
    /// <param name="target">The imported destination allocation and graph handle.</param>
    /// <param name="bindings">Frozen external allocation bindings, including the target.</param>
    public static void AppendResident2D(this RenderGraphBuilder graph, PreparedDrawingFrame2D frame, RenderTargetBinding2D target, DrawingGraphBindings2D bindings)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bindings);
        if (!ReferenceEquals(graph.Device, frame.Device) || !ReferenceEquals(graph, bindings.Graph))
        {
            throw new ArgumentException("Frame and bindings must belong to the parent graph device.");
        }

        // Resolve before mutation. The callback never reads a live scene or mutable bindings.
        IGraphicsTexture allocation = bindings.GetAllocation(target.Texture);
        DrawingGraphBindings2D snapshot = bindings.Snapshot();
        RenderGraphPass pass = graph.AddPass("drawing2d.resident", context => frame.Record(context.Commands));
        var declarations = new DrawingResourceDeclarations2D(pass, target, snapshot);
        frame.DeclareGraphResources(declarations, allocation);
        declarations.Apply();
        pass.UseTexture(target.Texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, frame.ClearsTarget ? ResourceAccess.ColorWrite : ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
    }
}
