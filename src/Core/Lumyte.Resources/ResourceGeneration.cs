using Lumyte.Memory;

namespace Lumyte.Resources;

internal sealed class ResourceGeneration(ResourceNode node, ulong revision, ResourcePipeline pipeline)
{
    internal ResourceNode Node { get; } = node;

    internal ulong Revision { get; } = revision;

    internal ResourcePipeline Pipeline { get; } = pipeline;

    internal object? Value { get; set; }

    internal int Pins { get; set; } = 1;

    internal bool Built { get; set; }

    internal bool Failed { get; set; }

    internal bool Releasing { get; set; }

    internal bool Released { get; set; }

    internal bool OutputReleased { get; set; }

    internal Dictionary<ResourceNode, ResourceDependency> Dependencies { get; } = [];

    internal Dictionary<ResourceId, ResourceSourceUse> Sources { get; } = [];

    internal Dictionary<string, ResourceRuntimeUse> Runtimes { get; } = new(StringComparer.Ordinal);

    internal List<ResourceCleanup> Cleanups { get; } = [];

    internal IMemoryScope? BuildMemory { get; set; }

    internal IMemoryScope? ResidentMemory { get; set; }
}
