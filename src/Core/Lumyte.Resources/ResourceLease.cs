namespace Lumyte.Resources;

/// <summary>Retains one exact content revision until it is returned.</summary>
/// <typeparam name="T">The resource output type.</typeparam>
/// <remarks>Use and disposal of the same lease must not overlap.</remarks>
public sealed class ResourceLease<T> : IDisposable
    where T : class
{
    private readonly T _value;
    private readonly Func<ResourceUnavailableException?> _getUnavailable;
    private Action? _release;

    internal ResourceLease(ResourceKey<T> key, ulong revision, T value, Func<ResourceUnavailableException?> getUnavailable, Action release)
    {
        (Key, Revision, _value, _getUnavailable, _release) = (key, revision, value, getUnavailable, release);
    }

    /// <summary>Gets the logical key of the retained resource.</summary>
    public ResourceKey<T> Key { get; }

    /// <summary>Gets the exact content revision retained by this lease.</summary>
    public ulong Revision { get; }

    /// <summary>Gets a value indicating whether the lease is retained and its runtime dependencies remain usable.</summary>
    public bool IsUsable => Volatile.Read(ref _release) is not null && _getUnavailable() is null;

    /// <summary>Gets the retained value, rejecting returned leases and invalidated runtime dependencies.</summary>
    /// <exception cref="ObjectDisposedException">This lease has been returned.</exception>
    /// <exception cref="ResourceUnavailableException">A required runtime generation is unavailable.</exception>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
            if (_getUnavailable() is { } unavailable)
            {
                throw unavailable;
            }

            return _value;
        }
    }

    /// <summary>Returns the revision's ownership once without I/O, resource destruction or GPU waits.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
