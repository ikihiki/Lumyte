namespace Lumyte.Resources;

internal sealed class ResourceBuildSlot(ResourceGeneration generation)
{
    internal ResourceGeneration Generation { get; } = generation;

    // Completed outside the manager gate so internal bookkeeping can continue without a ThreadPool relay.
    internal TaskCompletionSource<ResourceGeneration> Completion { get; } = new();
}
