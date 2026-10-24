namespace Lumyte.Resources;

/// <summary>Transforms a borrowed input using its declared execution environment.</summary>
/// <typeparam name="TInput">The primary input type.</typeparam>
/// <typeparam name="TOutput">The completed output type.</typeparam>
/// <typeparam name="TRuntime">The required environment contract.</typeparam>
public interface IResourceTransform<TInput, TOutput, TRuntime> : IResourceTransform<TInput, TOutput>, IResourceStage<TRuntime>
    where TInput : class
    where TOutput : class
    where TRuntime : class, IResourceRuntime
{
    /// <inheritdoc/>
    async ValueTask<object> IResourceStage.BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        var key = new ResourceKey<TInput>(GetInput(context.Address));
        TInput input = await context.RequireAsync(key, InputLifetime, cancellationToken);
        return await BuildAsync(input, (TRuntime)context.Runtime, context, cancellationToken);
    }

    /// <inheritdoc/>
    ValueTask<TOutput> IResourceTransform<TInput, TOutput>.BuildAsync(TInput input, ResourceBuildContext context, CancellationToken cancellationToken)
        => BuildAsync(input, (TRuntime)context.Runtime, context, cancellationToken);

    /// <summary>Builds an owned output using the selected, validated environment.</summary>
    /// <param name="input">The borrowed primary input.</param>
    /// <param name="runtime">The environment selected for this output generation.</param>
    /// <param name="context">The tracked dependencies, memory and cleanup for this build.</param>
    /// <param name="cancellationToken">Cancels the manager-owned build.</param>
    /// <returns>The separately owned output, ready for use.</returns>
    ValueTask<TOutput> BuildAsync(TInput input, TRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken);
}
