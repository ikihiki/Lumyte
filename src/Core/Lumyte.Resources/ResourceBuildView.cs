namespace Lumyte.Resources;

internal sealed class ResourceBuildView(
    Dictionary<ResourceNode, ResourceGeneration> published,
    Dictionary<ResourceNode, ResourceMetadata> metadata,
    ulong publicationVersion,
    HashSet<ResourceNode> forced,
    CancellationToken cancellationToken)
{
    internal Dictionary<ResourceNode, ResourceGeneration> Published { get; } = published;

    internal Dictionary<ResourceNode, ResourceMetadata> Metadata { get; } = metadata;

    internal ulong PublicationVersion { get; } = publicationVersion;

    internal HashSet<ResourceNode> Forced { get; } = forced;

    internal CancellationToken CancellationToken { get; } = cancellationToken;

    internal Dictionary<ResourceNode, ResourceBuildSlot> Builds { get; } = [];

    internal Dictionary<ResourceNode, HashSet<ResourceNode>> Edges { get; } = [];

    internal Dictionary<ResourceId, string> SourceRevisions { get; } = [];

    internal int Roots { get; set; }
}
