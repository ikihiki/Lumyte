namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders FillCircle commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultFillCircleRenderer2D : IFillCircleRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultFillCircleRenderer2D"/> class.</summary>
    public DefaultFillCircleRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IFillCircleRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<FillCircleCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
