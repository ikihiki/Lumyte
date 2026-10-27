namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders DrawPaintGraph commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultDrawPaintGraphRenderer2D : IDrawPaintGraphRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultDrawPaintGraphRenderer2D"/> class.</summary>
    public DefaultDrawPaintGraphRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IDrawPaintGraphRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<DrawPaintGraphCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
