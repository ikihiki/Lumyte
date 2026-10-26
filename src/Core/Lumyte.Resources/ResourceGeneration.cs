using Lumyte.Memory;

namespace Lumyte.Resources;

internal sealed class ResourceGeneration(ResourceNode node, ulong revision, IResourceStage stage, IResourceRuntime runtime, IResourceExecutor executor)
{
    internal ResourceNode Node { get; } = node;

    internal ulong Revision { get; } = revision;

    internal IResourceStage Stage { get; } = stage;

    internal IResourceRuntime Runtime { get; } = runtime;

    internal IResourceExecutor Executor { get; } = executor;

    internal object? Value { get; set; }

    internal int Pins { get; set; } = 1;

    internal ResourceGenerationState State { get; set; } = ResourceGenerationState.Building;

    internal bool RegistrationsClosed { get; set; }

    internal ResourceGenerationCleanupPhase CleanupPhase { get; set; } = ResourceGenerationCleanupPhase.Output;

    internal Dictionary<ResourceNode, ResourceDependency> Dependencies { get; } = [];

    internal Dictionary<ResourceId, ResourceSourceUse> Sources { get; } = [];

    internal Dictionary<string, ResourceRuntimeUse> Runtimes { get; } = new(StringComparer.Ordinal);

    internal (string Id, ulong Version)[] ResidentRuntimes { get; set; } = [];

    internal List<ResourceCleanup> Cleanups { get; } = [];

    internal IMemoryScope? BuildMemory { get; set; }

    internal IMemoryScope? ResidentMemory { get; set; }
}
