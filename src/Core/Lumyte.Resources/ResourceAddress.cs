namespace Lumyte.Resources;

/// <summary>Selects one logical element and conversion variant within a source resource.</summary>
public readonly record struct ResourceAddress
{
    /// <summary>Initializes a new instance of the <see cref="ResourceAddress"/> struct.</summary>
    /// <param name="id">The source-relative resource identifier.</param>
    /// <param name="subresource">The stage-defined element selector; it is not a filesystem path.</param>
    /// <param name="variant">The stable identifier for conversion settings.</param>
    public ResourceAddress(ResourceId id, string subresource = "", string variant = "")
    {
        if (id.Value is null)
        {
            throw new ArgumentException("Supply an initialized resource identifier.", nameof(id));
        }

        Id = id;
        Subresource = subresource;
        Variant = variant;
    }

    /// <summary>Gets the source-relative identifier.</summary>
    public ResourceId Id { get; }

    /// <summary>Gets the element selector.</summary>
    public string Subresource { get; }

    /// <summary>Gets the conversion variant.</summary>
    public string Variant { get; }
}
