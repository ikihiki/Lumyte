namespace Lumyte.Resources;

/// <summary>Dispatches manager-owned construction and release procedures.</summary>
/// <remarks>
/// Build and release use the same CPU scheduler; native owner operations are dispatched by the environment.
/// The manager can release a failed output directly inside its construction job. ReleaseAsync is the
/// dispatch entry for release outside that job, not a hook required for every destruction.
/// Await actual completion and never detach work.
/// </remarks>
public interface IResourceExecutor
{
    /// <summary>Gets a value indicating whether the current admitted execution slice should yield before another stage.</summary>
    bool ShouldYield => false;

    /// <summary>Runs the context's complete construction procedure inside the selected scheduler.</summary>
    /// <param name="context">Call its ExecuteAsync exactly once inside the job and await actual completion.</param>
    /// <param name="cancellationToken">The manager-owned build cancellation token.</param>
    /// <returns>Completion of construction, dependency draining and cleanup.</returns>
    ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken);

    /// <summary>Runs destruction using the environment retained when the output was constructed.</summary>
    /// <param name="stage">The original stage.</param>
    /// <param name="runtime">The original stable environment; use the output's retained physical epoch.</param>
    /// <param name="resource">The owned output.</param>
    /// <returns>Completion of the actual destruction attempt.</returns>
    ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource);
}
