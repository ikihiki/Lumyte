namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders FillRect commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultFillRectRenderer2D : IFillRectRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultFillRectRenderer2D"/> class.</summary>
    public DefaultFillRectRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IFillRectRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<FillRectCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedRectangleBatch2D.PrepareAsync(context, commands, cancellationToken);
}
