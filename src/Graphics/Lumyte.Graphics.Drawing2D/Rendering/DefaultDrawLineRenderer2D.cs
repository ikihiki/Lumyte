namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders DrawLine commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultDrawLineRenderer2D : IDrawLineRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultDrawLineRenderer2D"/> class.</summary>
    public DefaultDrawLineRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IDrawLineRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<DrawLineCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
