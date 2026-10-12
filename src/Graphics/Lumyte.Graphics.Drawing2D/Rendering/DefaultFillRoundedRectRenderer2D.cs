namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders FillRoundedRect commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultFillRoundedRectRenderer2D : IFillRoundedRectRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultFillRoundedRectRenderer2D"/> class.</summary>
    public DefaultFillRoundedRectRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IFillRoundedRectRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<FillRoundedRectCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
