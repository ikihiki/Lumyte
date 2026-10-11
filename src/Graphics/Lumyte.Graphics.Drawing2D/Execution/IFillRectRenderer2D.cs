namespace Lumyte.Graphics.Drawing2D;

/// <summary>Prepares consecutive FillRect commands using the configured rendering strategy.</summary>
public interface IFillRectRenderer2D
{
    /// <summary>Prepares a batch without submitting or waiting for GPU work.</summary>
    /// <param name="context">The borrowed graph, device, bindings, and target.</param>
    /// <param name="commands">The immutable consecutive command snapshot.</param>
    /// <param name="cancellationToken">The preparation cancellation token.</param>
    /// <returns>An owned batch whose passes can be appended once.</returns>
    ValueTask<IPreparedCommandBatch2D> PrepareAsync(
        RenderPreparationContext2D context,
        ReadOnlyMemory<FillRectCommand2D> commands,
        CancellationToken cancellationToken = default);
}
