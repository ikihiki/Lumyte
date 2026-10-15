namespace Lumyte.Resources;

/// <summary>Identifies a resource by address and its exact output type.</summary>
/// <typeparam name="T">The pipeline's exact output type.</typeparam>
public readonly record struct ResourceKey<T>
    where T : class
{
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
