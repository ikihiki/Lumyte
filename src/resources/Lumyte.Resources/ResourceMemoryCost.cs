namespace Lumyte.Resources;

public readonly record struct ResourceMemoryCost(
    ResourceMemoryPool Pool,
    long Bytes);
