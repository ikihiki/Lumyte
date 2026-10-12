using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Owns a prepared command batch and declares its ordered resource accesses.</summary>
internal interface IPreparedGraphCommandBatch2D : IDisposable
{
    /// <summary>Appends passes once to the graph and target used for preparation.</summary>
    /// <param name="graph">The preparation graph.</param>
    /// <param name="target">The preparation target.</param>
    void AppendPasses(RenderGraphBuilder graph, RenderTargetBinding2D target);
}
