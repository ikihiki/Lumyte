using Lumyte.Memory;

namespace Lumyte.Resources;

internal sealed class ResourceGeneration(ResourceNode node, ulong revision, IResourceStage stage)
{
    internal ResourceNode Node { get; } = node;

    internal ulong Revision { get; } = revision;

    internal IResourceStage Stage { get; } = stage;

    internal object? Value { get; set; }

    internal int Pins { get; set; } = 1;

    internal ResourceGenerationState State { get; set; } = ResourceGenerationState.Building;

    internal ResourceGenerationCleanupPhase CleanupPhase { get; set; } = ResourceGenerationCleanupPhase.Output;

    internal Dictionary<ResourceNode, ResourceDependency> Dependencies { get; } = [];

    internal Dictionary<ResourceId, ResourceSourceUse> Sources { get; } = [];

    internal List<ResourceCleanup> Cleanups { get; } = [];

    internal IMemoryScope? BuildMemory { get; set; }

    internal IMemoryScope? ResidentMemory { get; set; }
}
