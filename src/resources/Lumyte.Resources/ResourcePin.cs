namespace Lumyte.Resources;

/// <summary>Keeps one asset loaded until the pin is released.</summary>
public sealed class ResourcePin<T> : IAsyncDisposable
    where T : notnull
{
    private ResourceStore? store;

    internal ResourcePin(ResourceStore store, ResourceHandle<T> handle)
    {
        this.store = store;
        Handle = handle;
        store.AddStrongReference(handle.Id.Slot);
    }

    public ResourceHandle<T> Handle { get; }

    public ValueTask DisposeAsync()
    {
        ResourceStore? ownedStore = Interlocked.Exchange(ref store, null);
        ownedStore?.RemoveStrongReference(Handle.Id.Slot);
        return ValueTask.CompletedTask;
    }
}
