namespace Lumyte.Resources;

/// <summary>Manages the execution environment used to construct and destroy resources.</summary>
/// <remarks>
/// An environment coordinates job dispatch, owner access and any device services needed by its stages.
/// Device environments acquire current epochs for each build and can also implement <see cref="IResourceRuntimeDependency"/>.
/// The manager borrows the stable environment, captures it for each output generation and finishes cleanup before it stops.
/// </remarks>
public interface IResourceRuntime
{
    /// <summary>Dispatches a pipeline segment and returns its actual completed output.</summary>
    /// <param name="stage">The borrowed stage to execute.</param>
    /// <param name="context">The manager-owned construction context.</param>
    /// <param name="cancellationToken">The shared build's cancellation token.</param>
    /// <returns>The owned output. Started work must finish before this operation completes.</returns>
    /// <remarks>Invoke <see cref="ResourceBuildContext.ExecutePipelineAsync"/> inside the job to share it with same-runtime dependencies.</remarks>
    ValueTask<object> BuildAsync(IResourceStage stage, ResourceBuildContext context, CancellationToken cancellationToken);

    /// <summary>Dispatches destruction of an output, preserving failures for the manager to retry.</summary>
    /// <param name="stage">The stage that created the output.</param>
    /// <param name="resource">The output whose original owner and device epoch must be used.</param>
    /// <returns>Completion of the actual destruction attempt.</returns>
    ValueTask ReleaseAsync(IResourceStage stage, object resource);
}
