namespace Lumyte.Resources;

public sealed record ResourceLoadBatchOptions
{
    public ResourceLoadPriority Priority { get; init; } = ResourceLoadPriority.Normal;
}
