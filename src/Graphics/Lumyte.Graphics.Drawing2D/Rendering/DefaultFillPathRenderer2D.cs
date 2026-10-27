namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders path fills with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultFillPathRenderer2D : IFillPathRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultFillPathRenderer2D"/> class.</summary>
    public DefaultFillPathRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IFillPathRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<FillPathCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
