namespace Lumyte.Resources;

/// <summary>Executes resource construction and destruction while the manager owns their dependencies.</summary>
/// <remarks>The manager borrows this service and must finish its cleanup before the runtime stops.</remarks>
public interface IResourceRuntime
{
    /// <summary>Dispatches a stage and returns its actual completed output.</summary>
    /// <param name="stage">The borrowed stage to execute.</param>
    /// <param name="context">The manager-owned construction context.</param>
    /// <param name="cancellationToken">The shared build's cancellation token.</param>
    /// <returns>The owned output. Started work must finish before this operation completes.</returns>
    ValueTask<object> BuildAsync(IResourceStage stage, ResourceBuildContext context, CancellationToken cancellationToken);

    /// <summary>Dispatches destruction of an output, preserving failures for the manager to retry.</summary>
    /// <param name="stage">The stage that created the output.</param>
    /// <param name="resource">The output whose original owner and device epoch must be used.</param>
    /// <returns>Completion of the actual destruction attempt.</returns>
    ValueTask ReleaseAsync(IResourceStage stage, object resource);
}
