namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Exposes explicit segmented preparation without adding submission to Prepare or Record.</summary>
public interface IAsyncPipelineRenderer2D : IRenderer2D
{
    /// <summary>Prepares only immutable capture and the first CPU interval, without submission.</summary>
    /// <param name="list">The immutable logical drawing.</param>
    /// <param name="target">The matching borrowed target.</param>
    /// <param name="cancellationToken">Cooperative preparation cancellation.</param>
    /// <returns>The existing generic execution bridge and its ownership.</returns>
    ValueTask<PreparedPipelineExecution2D> PrepareExecutionAsync(DrawList2D list, RenderTarget2D target, CancellationToken cancellationToken = default);
}
