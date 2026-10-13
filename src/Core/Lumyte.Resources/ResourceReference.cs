namespace Lumyte.Resources;

/// <summary>Owns a logical resource slot whose published content may change during hot reload.</summary>
/// <typeparam name="T">The resource output type.</typeparam>
/// <remarks>Use and disposal of the same reference must not overlap.</remarks>
public sealed class ResourceReference<T> : IDisposable
    where T : class
{
    private readonly ResourceHandle<T> _handle;
    private readonly Func<bool> _isAvailable;
    private Action? _release;

    internal ResourceReference(ResourceKey<T> key, ResourceHandle<T> handle, Func<bool> isAvailable, Action release)
    {
        (Key, _handle, _isAvailable, _release) = (key, handle, isAvailable, release);
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

    /// <summary>Returns the logical ownership once without I/O, invalidating the slot after its final owner returns.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
