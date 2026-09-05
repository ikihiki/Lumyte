namespace Lumyte.Resources;

/// <summary>Writes a typed subresource selection into an asset key.</summary>
public interface IResourceSelector<T>
{
    void WriteTo(ResourceSelectorBuilder builder);
}
