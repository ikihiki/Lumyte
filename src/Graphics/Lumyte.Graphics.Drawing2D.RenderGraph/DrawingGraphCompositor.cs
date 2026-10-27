namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal static class DrawingGraphCompositor
{
    internal static ValueTask<IPreparedGraphCommandBatch2D> PrepareAsync(GraphPreparationContext2D context, GraphImageSource2D source, DrawingState2D state, float opacity = 1, GraphImageSource2D? original = null, bool replace = false, CancellationToken ct = default)
        => PreparedGraphGpuBatch2D.PrepareGraphAsync(context, source, state, opacity, original, replace, ct);
}
