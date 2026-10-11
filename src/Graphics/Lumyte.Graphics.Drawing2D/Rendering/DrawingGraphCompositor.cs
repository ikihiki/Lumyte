namespace Lumyte.Graphics.Drawing2D;

internal static class DrawingGraphCompositor
{
    internal static ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, GraphImageSource2D source, DrawingState2D state, float opacity = 1, GraphImageSource2D? original = null, bool replace = false, CancellationToken ct = default)
        => PreparedGpuBatch2D.PrepareGraphAsync(context, source, state, opacity, original, replace, ct);
}
