namespace Lumyte.Resources;

/// <summary>Builds a typed output using its declared execution environment.</summary>
/// <typeparam name="T">The completed output type.</typeparam>
/// <typeparam name="TRuntime">The required environment contract.</typeparam>
public interface IResourceSourceStage<T, TRuntime> : IResourceSourceStage<T>, IResourceStage<TRuntime>
    where T : notnull
    where TRuntime : class, IResourceRuntime
{
    /// <inheritdoc/>
    async ValueTask<object> IResourceStage.BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        => await BuildAsync((TRuntime)context.Runtime, context, cancellationToken);

    /// <inheritdoc/>
    ValueTask<T> IResourceSourceStage<T>.BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        => BuildAsync((TRuntime)context.Runtime, context, cancellationToken);

    /// <summary>Builds an owned output using the selected, validated environment.</summary>
    /// <param name="runtime">The environment selected for this output generation.</param>
    /// <param name="context">The tracked dependencies, memory and cleanup for this build.</param>
    /// <param name="cancellationToken">Cancels the manager-owned build.</param>
    /// <returns>The completed output, ready for use.</returns>
    ValueTask<T> BuildAsync(TRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken);

    /// <inheritdoc/>
    ValueTask IResourceStage.ReleaseAsync(object resource, IResourceRuntime runtime)
        => ReleaseAsync((T)resource, (TRuntime)runtime);

    /// <inheritdoc/>
    ValueTask IResourceSourceStage<T>.ReleaseAsync(T resource)
        => throw new InvalidOperationException("Release this stage through its selected runtime.");

    /// <summary>Releases an output using its original selected environment.</summary>
    /// <param name="resource">The owned output retaining its original device epoch.</param>
    /// <param name="runtime">The environment selected when the output was built.</param>
    /// <returns>The actual destruction completion.</returns>
    ValueTask ReleaseAsync(T resource, TRuntime runtime);
}
