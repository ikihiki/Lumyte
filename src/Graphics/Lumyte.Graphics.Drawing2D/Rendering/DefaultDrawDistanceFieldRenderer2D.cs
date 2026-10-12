namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders DrawDistanceField commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultDrawDistanceFieldRenderer2D : IDrawDistanceFieldRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultDrawDistanceFieldRenderer2D"/> class.</summary>
    public DefaultDrawDistanceFieldRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IDrawDistanceFieldRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<DrawDistanceFieldCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
