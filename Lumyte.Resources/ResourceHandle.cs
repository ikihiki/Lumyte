namespace Lumyte.Resources;

/// <summary>Provides typed access to one resource owned by a resource store.</summary>
public readonly record struct ResourceHandle<T>
    where T : notnull
{
    internal ResourceHandle(AssetKey<T> key, T value, uint slot)
    {
        Key = key;
        Value = value;
        Slot = slot;
    }

    public AssetKey<T> Key { get; }

    public T Value { get; }

    internal uint Slot { get; }
}
