namespace Lumyte.Resources;

/// <summary>Owns the loaded-state requirements of a group of assets.</summary>
public sealed class ResourceScope : IAsyncDisposable
{
    private readonly ResourceStore store;
    private readonly ResourceScopeOptions options;
    private readonly HashSet<uint> slots = [];
    private readonly Lock slotsLock = new();
    private int disposed;

    internal ResourceScope(ResourceStore store, ResourceScopeOptions options)
    {
        this.store = store;
        this.options = options;
    }

    public async ValueTask<ResourceHandle<T>> LoadAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ResourceHandle<T> handle = await store
            .LoadAsync(key, cancellationToken)
            .ConfigureAwait(false);
        uint[] retainedSlots = store.GetDependencyClosure(handle.Id.Slot);
        lock (slotsLock)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            foreach (uint slot in retainedSlots)
            {
                if (slots.Add(slot))
                {
                    store.AddStrongReference(slot);
                }
            }
        }

        return handle;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        uint[] ownedSlots;
        lock (slotsLock)
        {
            ownedSlots = [.. slots];
            slots.Clear();
        }

        foreach (uint slot in ownedSlots)
        {
            store.RemoveStrongReference(slot);
        }

        if (options.UnloadUnusedOnDispose)
        {
            await store
                .CollectUnusedAsync(ownedSlots)
                .ConfigureAwait(false);
        }
    }
}
