namespace Lumyte.Graphics.Drawing2D.Tests;

// Explicit controls for tests of the retained legacy interpreter/rectangle
// instrumentation and its two-stream ABI. Defaults in production use records.
internal static class LegacyDrawRenderers2D
{
    internal static DrawingCommandRenderers2D Create()
    {
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        return new(new Rectangle(), defaults.FillRoundedRect, new Circle(), defaults.DrawLine, defaults.FillPath, defaults.StrokePath, new Image(), defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
    }

    private sealed class Rectangle : IFillRectRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
            => PreparedRectangleBatch2D.PrepareAsync(context, commands, cancellationToken);
    }

    private sealed class Circle : IFillCircleRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken)
            => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
    }

    private sealed class Image : IDrawImageRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken)
            => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
    }
}
