namespace Lumyte.Pipelines;

/// <summary>A compiled reusable typed pipeline with independently owned results.</summary>
/// <typeparam name="TInput">The immutable input payload.</typeparam>
/// <typeparam name="TOutput">The output payload.</typeparam>
public interface IPipeline<TInput, TOutput> : IDisposable
{
    /// <summary>Prepares a result without submitting graphics work.</summary>
    /// <param name="input">The immutable per-run input.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The independently owned result.</returns>
    ValueTask<PipelineResult<TOutput>> PrepareAsync(TInput input, CancellationToken cancellationToken = default);
}
