namespace Lumyte.Resources;

/// <summary>Identifies a typed resource without retaining managed state.</summary>
public readonly record struct ResourceId<T>
    where T : notnull
{
    internal ResourceId(uint slot)
    {
        Slot = slot;
    }

    internal uint Slot { get; }
}
