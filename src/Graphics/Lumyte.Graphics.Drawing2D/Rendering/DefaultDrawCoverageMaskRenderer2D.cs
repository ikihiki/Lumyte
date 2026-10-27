namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders DrawCoverageMask commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultDrawCoverageMaskRenderer2D : IDrawCoverageMaskRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultDrawCoverageMaskRenderer2D"/> class.</summary>
    public DefaultDrawCoverageMaskRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IDrawCoverageMaskRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<DrawCoverageMaskCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
