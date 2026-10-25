namespace Lumyte.Resources;

/// <summary>Owns a logical resource slot whose published content may change during hot reload.</summary>
/// <typeparam name="T">The resource output type.</typeparam>
/// <remarks>Use and disposal of the same reference must not overlap.</remarks>
public sealed class ResourceReference<T> : IDisposable
    where T : notnull
{
    private readonly ResourceHandle<T> _handle;
    private ResourceManager? _manager;

    internal ResourceReference(ResourceManager manager, ResourceKey<T> key, ResourceHandle<T> handle)
    {
        (_manager, Key, _handle) = (manager, key, handle);
    }

    /// <summary>Gets the logical key owned by this reference.</summary>
    public ResourceKey<T> Key { get; }

    /// <summary>Gets a value indicating whether this reference is retained and its current published value is available.</summary>
    public bool IsAvailable => Volatile.Read(ref _manager) is { } manager && manager.IsHandleAvailable(_handle);

    /// <summary>Gets the nonowning handle to the logical resource slot.</summary>
    /// <exception cref="ObjectDisposedException">This reference has been returned.</exception>
    public ResourceHandle<T> Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _manager) is null, this);
            return _handle;
        }
    }

    /// <summary>Pins the current value and revision for an operation such as one rendering frame.</summary>
    /// <param name="snapshot">An optional resource snapshot whose captured publication supplies the value.</param>
    /// <returns>An independently owned lease whose value does not switch on hot reload.</returns>
    /// <exception cref="ObjectDisposedException">This reference has been returned.</exception>
    /// <exception cref="InvalidOperationException">No usable published value is available.</exception>
    public ResourceLease<T> Acquire(ResourceSnapshot? snapshot = null)
        => TryAcquire(out ResourceLease<T>? lease, snapshot)
            ? lease!
            : throw new InvalidOperationException("The reference has no usable published value.");

    /// <summary>Attempts to pin a value without I/O or pipeline execution.</summary>
    /// <param name="lease">The retained value and revision, or null when unavailable.</param>
    /// <param name="snapshot">An optional resource snapshot whose captured publication supplies the value.</param>
    /// <returns>Whether a value could be retained.</returns>
    /// <exception cref="ObjectDisposedException">This reference has been returned.</exception>
    public bool TryAcquire(out ResourceLease<T>? lease, ResourceSnapshot? snapshot = null)
    {
        ResourceManager? manager = Volatile.Read(ref _manager);
        ObjectDisposedException.ThrowIf(manager is null, this);
        if (snapshot is not null)
        {
            return snapshot.TryAcquire(_handle, out lease);
        }

        lease = manager.AcquireCurrentLease(_handle);
        return lease is not null;
    }

    /// <summary>Returns the logical ownership once without I/O, invalidating the slot after its final owner returns.</summary>
    public void Dispose() => Interlocked.Exchange(ref _manager, null)?.ReturnReference(_handle);
}
