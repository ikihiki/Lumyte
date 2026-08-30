namespace Lumyte.Resources;

public sealed record ResourceStoreOptions
{
    public IDictionary<ResourceMemoryPool, long> MemoryBudgets { get; init; } =
        new Dictionary<ResourceMemoryPool, long>();
}
