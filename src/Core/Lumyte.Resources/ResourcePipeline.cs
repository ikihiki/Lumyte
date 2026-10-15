namespace Lumyte.Resources;

/// <summary>Provides immutable pipeline metadata and adapts typed, dependency-injected stages.</summary>
public abstract class ResourcePipeline
{
    /// <summary>Initializes a new instance of the <see cref="ResourcePipeline"/> class.</summary>
    /// <param name="outputType">The exact reference type produced by this pipeline.</param>
    /// <param name="inputType">The primary input's exact type, or null for a source stage.</param>
    protected ResourcePipeline(Type outputType, Type? inputType = null)
    {
        ArgumentNullException.ThrowIfNull(outputType);
        if (outputType.IsValueType || outputType.ContainsGenericParameters)
        {
            throw new ArgumentException("A pipeline output must be a closed reference type.", nameof(outputType));
        }

        if (inputType is not null)
        {
            if (inputType.IsValueType || inputType.ContainsGenericParameters)
            {
                throw new ArgumentException("A pipeline input must be a closed reference type.", nameof(inputType));
            }
        }

        OutputType = outputType;
        InputType = inputType;
    }

    /// <summary>Gets the exact output type.</summary>
    public Type OutputType { get; }

    /// <summary>Gets the runtime identity used to register and select this pipeline.</summary>
    public RuntimeTypeHandle OutputTypeHandle => OutputType.TypeHandle;

    /// <summary>Gets the exact primary input type, or null.</summary>
    public Type? InputType { get; }

    /// <summary>Builds an owned output using tracked input and auxiliary dependencies.</summary>
    /// <param name="context">The current construction context.</param>
    /// <param name="cancellationToken">Cancels the manager-owned build.</param>
    /// <returns>The completed nonnull output.</returns>
    public abstract ValueTask<object> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken);

    /// <summary>Releases a previously completed output.</summary>
    /// <param name="resource">The output owned by the manager.</param>
    /// <returns>The release completion.</returns>
    public abstract ValueTask ReleaseAsync(object resource);
}
