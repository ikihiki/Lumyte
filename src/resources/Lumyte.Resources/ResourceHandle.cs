namespace Lumyte.Resources;

/// <summary>Provides typed access to the current generation of one resource.</summary>
public readonly record struct ResourceHandle<T>
    where T : notnull
{
    private readonly ResourceStore? store;

    internal ResourceHandle(AssetKey<T> key, ResourceStore store, uint slot)
    {
        Key = key;
        this.store = store;
        Id = new ResourceId<T>(slot);
    }

    public AssetKey<T> Key { get; }

    public ResourceId<T> Id { get; }

    /// <summary>Whether this handle was created by a resource store.</summary>
    public bool IsValid => store is not null;

    public T Value => RequireStore().GetCurrent(Id).Value;

    /// <summary>Gets the generation loaded for this handle.</summary>
    public uint Generation => RequireStore().GetCurrent(Id).Generation;

    public bool TryGetValue(out T? value)
    {
        if (store is not null
            && store.TryGetCurrent(Id, out ResourceRecord<T>? record)
            && record is not null)
        {
            value = record.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Tries to retain the currently loaded generation without snapshotting the store.</summary>
    public bool TryAcquireLease(out ResourceLease<T>? lease)
    {
        if (store is not null) { return store.TryAcquireLease(Id, out lease); }
        lease = null;
        return false;
    }

    internal ResourceStore? Store => store;

    private ResourceStore RequireStore() => store
        ?? throw new InvalidOperationException("The resource handle is not initialized.");
}
