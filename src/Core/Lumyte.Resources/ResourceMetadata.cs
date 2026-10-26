namespace Lumyte.Resources;

internal sealed record ResourceMetadata(
    IReadOnlyCollection<ResourceNode> Dependencies,
    IReadOnlyDictionary<ResourceId, string> Sources,
    IReadOnlyDictionary<string, ResourceRuntimeUse> Runtimes);
