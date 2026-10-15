using System.Text.Json;

namespace Lumyte.Resources.Sample;

internal sealed class SceneDocument(ResourceId id, JsonDocument document) : IResourceDocument
{
    public ResourceId Id => id;

    public IReadOnlyList<ResourceSubresource> Subresources { get; } = Array.AsReadOnly<ResourceSubresource>(
    [
        new() { Selector = "mesh/0", OutputTypes = [typeof(MeshInfo).TypeHandle] },
        new() { Selector = "material/0", OutputTypes = [typeof(MaterialInfo).TypeHandle] },
    ]);

    internal JsonElement Root => document.RootElement;

    internal void Dispose() => document.Dispose();
}
