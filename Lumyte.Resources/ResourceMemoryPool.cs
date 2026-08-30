namespace Lumyte.Resources;

public readonly record struct ResourceMemoryPool(string Name)
{
    public static ResourceMemoryPool Cpu { get; } = new("cpu");

    public static ResourceMemoryPool Gpu { get; } = new("gpu");
}
