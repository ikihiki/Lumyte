namespace Lumyte.Resources;

/// <summary>Builds a typed output without a fixed primary input pipeline.</summary>
/// <typeparam name="T">The completed output type.</typeparam>
public interface IResourceSourceStage<T> : IResourceStage
    where T : class
{
    /// <inheritdoc />
    Type IResourceStage.OutputType => typeof(T);

    /// <inheritdoc />
    Type? IResourceStage.InputType => null;

    /// <inheritdoc />
    async ValueTask<object> IResourceStage.BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        => await BuildAsync(context, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    ValueTask IResourceStage.ReleaseAsync(object resource) => ReleaseAsync((T)resource);

    /// <summary>Builds a complete nonnull output and transfers its ownership to the manager.</summary>
    /// <param name="context">The tracked sources, dependencies, memory and cleanup for this build.</param>
    /// <param name="cancellationToken">Cancels the manager-owned build.</param>
    /// <returns>The completed output, ready for use.</returns>
    new ValueTask<T> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken);

    /// <summary>Releases an output; retry after failure must not repeat already completed destruction.</summary>
    /// <param name="resource">The owned output to release.</param>
    /// <returns>The release completion.</returns>
    ValueTask ReleaseAsync(T resource);
}
