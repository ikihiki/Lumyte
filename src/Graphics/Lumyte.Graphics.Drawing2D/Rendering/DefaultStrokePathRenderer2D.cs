namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders StrokePath commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultStrokePathRenderer2D : IStrokePathRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultStrokePathRenderer2D"/> class.</summary>
    public DefaultStrokePathRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IStrokePathRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<StrokePathCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
