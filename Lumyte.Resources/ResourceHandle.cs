namespace Lumyte.Resources;

/// <summary>Provides typed access to one resource owned by a resource store.</summary>
public readonly record struct ResourceHandle<T>
    where T : notnull
{
    internal ResourceHandle(AssetKey<T> key, T value, uint slot, uint generation)
    {
        Key = key;
        Value = value;
        Slot = slot;
        Generation = generation;
    }

    public AssetKey<T> Key { get; }

    public T Value { get; }

    /// <summary>Gets the generation loaded for this handle.</summary>
    public uint Generation { get; }

    internal uint Slot { get; }
}
