namespace Lumyte.Resources;

/// <summary>Converts a borrowed primary input into a separately owned output.</summary>
/// <typeparam name="TInput">The primary input type.</typeparam>
/// <typeparam name="TOutput">The completed output type.</typeparam>
public interface IResourceTransform<TInput, TOutput> : IResourceStage
    where TInput : class
    where TOutput : class
{
    /// <inheritdoc />
    Type IResourceStage.OutputType => typeof(TOutput);

    /// <inheritdoc />
    Type? IResourceStage.InputType => typeof(TInput);

    /// <summary>Gets the lifetime of the primary input dependency.</summary>
    ResourceDependencyLifetime InputLifetime { get; }

    /// <inheritdoc />
    async ValueTask<object> IResourceStage.BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        var key = new ResourceKey<TInput>(GetInput(context.Address));
        TInput input = await context.RequireAsync(key, InputLifetime, cancellationToken);
        return await BuildAsync(input, context, cancellationToken);
    }

    /// <inheritdoc />
    ValueTask IResourceStage.ReleaseAsync(object resource) => ReleaseAsync((TOutput)resource);

    /// <summary>Deterministically maps an output address to its primary input address.</summary>
    /// <param name="output">The requested output address.</param>
    /// <returns>The input address, potentially using a different selector or variant.</returns>
    ResourceAddress GetInput(ResourceAddress output);

    /// <summary>Builds a complete output, including any required upload completion.</summary>
    /// <param name="input">The borrowed primary input; its ownership cannot be transferred.</param>
    /// <param name="context">The tracked dependencies, memory and cleanup for this build.</param>
    /// <param name="cancellationToken">Cancels the manager-owned build.</param>
    /// <returns>The separately owned output, ready for use.</returns>
    ValueTask<TOutput> BuildAsync(TInput input, ResourceBuildContext context, CancellationToken cancellationToken);

    /// <summary>Releases an output; retry after failure must preserve already completed cleanup.</summary>
    /// <param name="resource">The owned output to release.</param>
    /// <returns>The release completion.</returns>
    ValueTask ReleaseAsync(TOutput resource);
}
