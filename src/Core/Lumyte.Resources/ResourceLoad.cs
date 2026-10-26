namespace Lumyte.Resources;

internal sealed class ResourceLoad(ResourceBuildView view)
{
    internal ResourceBuildView View { get; } = view;

    internal TaskCompletionSource<ResourceGeneration> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int Waiters { get; set; }

    internal ResourceGeneration? Generation { get; set; }
}
