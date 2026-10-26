namespace Lumyte.Graphics.Drawing2D;

/// <summary>Renders DrawMesh commands with the standard GPU paint and coverage evaluator.</summary>
public sealed class DefaultDrawMeshRenderer2D : IDrawMeshRenderer2D
{
    /// <summary>Initializes a new instance of the <see cref="DefaultDrawMeshRenderer2D"/> class.</summary>
    public DefaultDrawMeshRenderer2D()
    {
    }

    /// <inheritdoc/>
    ValueTask<IPreparedCommandBatch2D> IDrawMeshRenderer2D.PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<DrawMeshCommand2D> commands,
        CancellationToken cancellationToken)
        => PreparedGpuBatch2D.PrepareAsync(context, commands, cancellationToken);
}
