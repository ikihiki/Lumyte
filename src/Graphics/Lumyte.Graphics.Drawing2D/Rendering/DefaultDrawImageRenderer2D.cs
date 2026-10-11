namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders DrawImage commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultDrawImageRenderer2D : IDrawImageRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultDrawImageRenderer2D"/> class.</summary>
    public DefaultDrawImageRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IDrawImageRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<DrawImageCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
