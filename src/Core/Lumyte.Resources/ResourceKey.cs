namespace Lumyte.Resources;

/// <summary>Identifies a resource by address and its exact output type.</summary>
/// <typeparam name="T">The pipeline's exact output type.</typeparam>
public readonly record struct ResourceKey<T>
    where T : notnull
{
    /// <summary>Initializes a new instance of the <see cref="ResourceKey{T}"/> struct from a source-relative path.</summary>
    /// <param name="id">The source-relative logical path.</param>
    /// <param name="subresource">The stage-defined element selector.</param>
    /// <param name="variant">The identifier for conversion settings.</param>
    public ResourceKey(string id, string subresource = "", string variant = "")
        : this(new ResourceAddress(new ResourceId(id), subresource, variant))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResourceKey{T}"/> struct from an existing source identifier.</summary>
    /// <param name="id">The source-relative resource identifier.</param>
    /// <param name="subresource">The stage-defined element selector.</param>
    /// <param name="variant">The identifier for conversion settings.</param>
    public ResourceKey(ResourceId id, string subresource = "", string variant = "")
        : this(new ResourceAddress(id, subresource, variant))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResourceKey{T}"/> struct.</summary>
    /// <param name="address">The source, element and variant to build.</param>
    public ResourceKey(ResourceAddress address)
    {
        if (address.Id.Value is null || address.Subresource is null || address.Variant is null)
        {
            throw new ArgumentException("Supply an initialized resource address.", nameof(address));
        }

        Address = address;
    }

    /// <summary>Gets the source, element and variant.</summary>
    public ResourceAddress Address { get; }

    /// <summary>Gets the runtime identity of the exact output type, used to select its pipeline.</summary>
    public RuntimeTypeHandle TypeHandle => typeof(T).TypeHandle;
}
