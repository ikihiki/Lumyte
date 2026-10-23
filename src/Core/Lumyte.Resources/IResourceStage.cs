namespace Lumyte.Resources;

/// <summary>Provides the common execution contract for a directly registered resource stage.</summary>
/// <remarks>
/// Implement a typed source stage or transform to obtain type-safe dispatch. The manager borrows the stage.
/// Use ordinary awaits in stage orchestration so the runtime can schedule continuations; do not block on asynchronous work.
/// </remarks>
public interface IResourceStage
{
    /// <summary>Gets the exact output type produced by this stage.</summary>
    Type OutputType { get; }

    /// <summary>Gets the exact primary input type, or null for a source stage.</summary>
    Type? InputType { get; }

    /// <summary>Builds an owned output with tracked dependencies.</summary>
    /// <param name="context">The construction context.</param>
    /// <param name="cancellationToken">Cancels the manager-owned build.</param>
    /// <returns>The completed nonnull output.</returns>
    ValueTask<object> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken);

    /// <summary>Releases an owned output, preserving progress when a retry is necessary.</summary>
    /// <param name="resource">The previously constructed output.</param>
    /// <returns>The release completion.</returns>
    ValueTask ReleaseAsync(object resource);
}
