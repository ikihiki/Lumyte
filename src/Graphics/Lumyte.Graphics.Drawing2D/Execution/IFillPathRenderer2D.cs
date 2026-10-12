namespace Lumyte.Graphics.Drawing2D;

/// <summary>Prepares consecutive FillPath commands using the configured rendering strategy.</summary>
public interface IFillPathRenderer2D
{
    /// <summary>Prepares a batch without submitting or waiting for GPU work.</summary>
    /// <param name="context">The borrowed device and target geometry.</param>
    /// <param name="commands">The immutable consecutive command snapshot.</param>
    /// <param name="cancellationToken">The preparation cancellation token.</param>
    /// <returns>An owned batch that can record commands once.</returns>
    ValueTask<IPreparedCommandBatch2D> PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<FillPathCommand2D> commands,
        CancellationToken cancellationToken = default);
}
