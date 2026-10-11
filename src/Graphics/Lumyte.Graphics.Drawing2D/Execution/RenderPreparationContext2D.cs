using Lumyte.Graphics.Abstractions;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Exposes borrowed resources during preparation of a typed command batch.</summary>
public sealed class RenderPreparationContext2D
{
    internal RenderPreparationContext2D(IGraphicDevice device, RenderGraphBuilder graph, DrawingGraphBindings2D bindings, RenderTargetBinding2D target)
    {
        Device = device;
        Graph = graph;
        Bindings = bindings;
        Target = target;
    }

    /// <summary>Gets the borrowed graphic device.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the graph receiving the batch's passes.</summary>
    public RenderGraphBuilder Graph { get; }

    /// <summary>Gets the image binding snapshot.</summary>
    public DrawingGraphBindings2D Bindings { get; }

    /// <summary>Gets the target normalized to Load after any host clear pass.</summary>
    public RenderTargetBinding2D Target { get; }
}
