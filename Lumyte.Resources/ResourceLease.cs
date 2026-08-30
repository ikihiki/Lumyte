namespace Lumyte.Resources;

/// <summary>Retains one specific resource generation.</summary>
public readonly record struct ResourceLease<T>
    where T : notnull
{
    internal ResourceLease(T value, uint generation)
    {
        Value = value;
        Generation = generation;
    }

    public T Value { get; }

    public uint Generation { get; }
}
