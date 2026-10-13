namespace Lumyte.Resources;

/// <summary>Provides immutable pipeline metadata and adapts typed, dependency-injected stages.</summary>
public abstract class ResourcePipeline
{
    /// <summary>Initializes a new instance of the <see cref="ResourcePipeline"/> class.</summary>
    /// <param name="id">The unique manager-local pipeline name.</param>
    /// <param name="outputType">The exact reference type produced by this pipeline.</param>
    /// <param name="inputPipelineId">The fixed primary input pipeline, or null for a source stage.</param>
    /// <param name="inputType">The primary input's exact type, or null for a source stage.</param>
    protected ResourcePipeline(string id, Type outputType, string? inputPipelineId = null, Type? inputType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(outputType);
        if (outputType.IsValueType || outputType.ContainsGenericParameters)
        {
            throw new ArgumentException("A pipeline output must be a closed reference type.", nameof(outputType));
        }

        if ((inputPipelineId is null) != (inputType is null))
        {
            throw new ArgumentException("Supply both the primary input pipeline and its type, or neither.", nameof(inputPipelineId));
        }

        if (inputPipelineId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(inputPipelineId);
            if (inputType!.IsValueType || inputType.ContainsGenericParameters)
            {
                throw new ArgumentException("A pipeline input must be a closed reference type.", nameof(inputType));
            }
        }

        Id = id;
        OutputType = outputType;
        InputPipelineId = inputPipelineId;
        InputType = inputType;
    }

    /// <summary>Gets the manager-local pipeline name.</summary>
    public string Id { get; }

    /// <summary>Gets the exact output type.</summary>
    public Type OutputType { get; }

    /// <summary>Gets the fixed primary input pipeline, or null.</summary>
    public string? InputPipelineId { get; }

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
