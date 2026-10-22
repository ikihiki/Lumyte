namespace Lumyte.Resources;

/// <summary>Retains one exact content revision until it is returned.</summary>
/// <typeparam name="T">The resource output type.</typeparam>
/// <remarks>Use and disposal of the same lease must not overlap.</remarks>
public sealed class ResourceLease<T> : IDisposable
    where T : class
{
    private readonly T _value;
    private Action? _release;

    internal ResourceLease(ResourceKey<T> key, ulong revision, T value, Action release)
    {
        (Key, Revision, _value, _release) = (key, revision, value, release);
    }

    /// <summary>Gets the logical key of the retained resource.</summary>
    public ResourceKey<T> Key { get; }

    /// <summary>Gets the exact content revision retained by this lease.</summary>
    public ulong Revision { get; }

    /// <summary>Gets a value indicating whether the lease is retained.</summary>
    public bool IsUsable => Volatile.Read(ref _release) is not null;

    /// <summary>Gets the retained value, rejecting returned leases.</summary>
    /// <exception cref="ObjectDisposedException">This lease has been returned.</exception>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
            return _value;
        }
    }

    /// <summary>Returns the revision's ownership once without I/O, resource destruction or GPU waits.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
