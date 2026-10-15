namespace Lumyte.Resources;

/// <summary>Exposes discovery metadata for independently requested elements of one document.</summary>
public interface IResourceDocument
{
    /// <summary>Gets the source-relative document identifier.</summary>
    ResourceId Id { get; }

    /// <summary>Gets immutable element metadata without eagerly constructing the outputs.</summary>
    IReadOnlyList<ResourceSubresource> Subresources { get; }
}
