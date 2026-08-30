namespace Lumyte.Resources;

public readonly record struct ResourceLoadOptions
{
    public ResourceLoadOptions()
    {
    }

    public ResourceLoadPriority Priority { get; init; } = ResourceLoadPriority.Normal;
}
