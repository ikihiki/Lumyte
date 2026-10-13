namespace Lumyte.Resources;

internal sealed class ResourceBuildSlot(ResourceGeneration generation)
{
    internal ResourceGeneration Generation { get; } = generation;

    internal TaskCompletionSource<ResourceGeneration> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
