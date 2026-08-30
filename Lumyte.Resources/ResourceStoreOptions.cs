namespace Lumyte.Resources;

public sealed record ResourceStoreOptions
{
    public ResourceSchedulingOptions Scheduling { get; init; } = new();

    public IDictionary<ResourceMemoryPool, long> MemoryBudgets { get; init; } =
        new Dictionary<ResourceMemoryPool, long>();
}
