using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Inserts configured layered drawing into a caller-owned parent render graph.</summary>
public static class Drawing2DRenderGraphExtensions
{
    /// <summary>Appends drawing and effect passes; never compiles or executes a nested graph.</summary>
    /// <param name="graph">The parent graph owning all handles and temporary allocations.</param>
    /// <param name="renderer">The configured layered drawing and target.</param>
    /// <param name="cancellationToken">The preparation cancellation token.</param>
    /// <returns>The fixed-resource owner, retained through GPU completion and plan disposal.</returns>
    public static ValueTask<PreparedDrawingPasses2D> Append2D(this RenderGraphBuilder graph, RenderGraphRenderer2D renderer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(renderer);
        return renderer.AppendAsync(graph, cancellationToken);
    }
}
