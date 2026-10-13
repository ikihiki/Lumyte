namespace Lumyte.Resources;

/// <summary>Identifies one explicitly selected, typed pipeline output.</summary>
/// <typeparam name="T">The pipeline's exact output type.</typeparam>
public readonly record struct ResourceKey<T>
    where T : class
{
    /// <summary>Initializes a new instance of the <see cref="ResourceKey{T}"/> struct.</summary>
    /// <param name="address">The source, element and variant to build.</param>
    /// <param name="pipelineId">The manager-local pipeline name.</param>
    public ResourceKey(ResourceAddress address, string pipelineId)
    {
        if (address.Id.Value is null || address.Subresource is null || address.Variant is null)
        {
            throw new ArgumentException("Supply an initialized resource address.", nameof(address));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);
        Address = address;
        PipelineId = pipelineId;
    }

    /// <summary>Gets the source, element and variant.</summary>
    public ResourceAddress Address { get; }

    /// <summary>Gets the manager-local pipeline name.</summary>
    public string PipelineId { get; }
}
