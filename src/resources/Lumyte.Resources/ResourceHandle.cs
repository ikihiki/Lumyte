namespace Lumyte.Resources;

/// <summary>Provides typed access to the current generation of one resource.</summary>
public readonly record struct ResourceHandle<T>
    where T : notnull
{
    private readonly ResourceStore store;

    internal ResourceHandle(AssetKey<T> key, ResourceStore store, uint slot)
    {
        Key = key;
        this.store = store;
        Id = new ResourceId<T>(slot);
    }

    public AssetKey<T> Key { get; }

    public ResourceId<T> Id { get; }

    public T Value => store.GetCurrent(Id).Value;

    /// <summary>Gets the generation loaded for this handle.</summary>
    public uint Generation => store.GetCurrent(Id).Generation;

    public bool TryGetValue(out T? value)
    {
        if (store.TryGetCurrent(Id, out ResourceRecord<T>? record)
            && record is not null)
        {
            value = record.Value;
            return true;
        }

        value = default;
        return false;
    }

    internal ResourceStore Store => store;
}
