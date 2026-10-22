namespace Lumyte.Resources;

internal sealed class ResourceSlot(ResourceNode node, uint generation)
{
    internal ResourceNode Node { get; } = node;

    internal uint Generation { get; } = generation;

    internal int References { get; set; }

    internal ResourceGeneration? Current { get; set; }
}
