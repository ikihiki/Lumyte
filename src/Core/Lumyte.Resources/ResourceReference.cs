namespace Lumyte.Resources;

/// <summary>Owns a logical resource slot whose published content may change during hot reload.</summary>
/// <typeparam name="T">The resource output type.</typeparam>
/// <remarks>Use and disposal of the same reference must not overlap.</remarks>
public sealed class ResourceReference<T> : IDisposable
    where T : class
{
    private readonly ResourceHandle<T> _handle;
    private readonly Func<bool> _isAvailable;
    private readonly Func<ResourceLease<T>?> _acquire;
    private Action? _release;

    internal ResourceReference(ResourceKey<T> key, ResourceHandle<T> handle, Func<bool> isAvailable, Func<ResourceLease<T>?> acquire, Action release)
    {
        (Key, _handle, _isAvailable, _acquire, _release) = (key, handle, isAvailable, acquire, release);
    }

    /// <summary>Gets the logical key owned by this reference.</summary>
    public ResourceKey<T> Key { get; }

    /// <summary>Gets a value indicating whether this reference is retained and its current published value is available.</summary>
    public bool IsAvailable => Volatile.Read(ref _release) is not null && _isAvailable();

    /// <summary>Gets the nonowning handle to the logical resource slot.</summary>
    /// <exception cref="ObjectDisposedException">This reference has been returned.</exception>
    public ResourceHandle<T> Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
            return _handle;
        }
    }

    /// <summary>Pins the current value and revision for an operation such as one rendering frame.</summary>
    /// <param name="scope">An optional captured publication to resolve against.</param>
    /// <returns>An independently owned lease whose value does not switch on hot reload.</returns>
    /// <exception cref="ObjectDisposedException">This reference has been returned.</exception>
    /// <exception cref="InvalidOperationException">No usable published value is available.</exception>
    public ResourceLease<T> Acquire(ResourceReadScope? scope = null)
        => TryAcquire(out ResourceLease<T>? lease, scope)
            ? lease!
            : throw new InvalidOperationException("The reference has no usable published value.");

    /// <summary>Attempts to pin a value without I/O or pipeline execution.</summary>
    /// <param name="lease">The retained value and revision, or null when unavailable.</param>
    /// <param name="scope">An optional captured publication to resolve against.</param>
    /// <returns>Whether a value could be retained.</returns>
    /// <exception cref="ObjectDisposedException">This reference has been returned.</exception>
    public bool TryAcquire(out ResourceLease<T>? lease, ResourceReadScope? scope = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
        if (scope is not null)
        {
            return scope.TryAcquire(_handle, out lease);
        }

        lease = _acquire();
        return lease is not null;
    }

    /// <summary>Returns the logical ownership once without I/O, invalidating the slot after its final owner returns.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
