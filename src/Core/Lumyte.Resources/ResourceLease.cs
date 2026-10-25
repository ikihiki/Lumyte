namespace Lumyte.Resources;

/// <summary>Retains one exact content revision until it is returned.</summary>
/// <typeparam name="T">The resource output type.</typeparam>
/// <remarks>Use and disposal of the same lease must not overlap.</remarks>
public sealed class ResourceLease<T> : IDisposable
    where T : notnull
{
    private readonly ResourceGeneration _generation;
    private ResourceManager? _manager;

    internal ResourceLease(ResourceManager manager, ResourceGeneration generation)
    {
        (_manager, _generation) = (manager, generation);
    }

    /// <summary>Gets the logical key of the retained resource.</summary>
    public ResourceKey<T> Key => new(_generation.Node.Address);

    /// <summary>Gets the exact content revision retained by this lease.</summary>
    public ulong Revision => _generation.Revision;

    /// <summary>Gets a value indicating whether the lease is retained and its runtime dependencies remain usable.</summary>
    public bool IsUsable => Volatile.Read(ref _manager) is { } manager && manager.IsUsable(_generation);

    /// <summary>Gets the retained value, rejecting returned leases and invalidated runtime dependencies.</summary>
    /// <remarks>A value-type output is copied without transferring ownership of its referenced resources.</remarks>
    /// <exception cref="ObjectDisposedException">This lease has been returned.</exception>
    /// <exception cref="ResourceUnavailableException">A required runtime generation is unavailable.</exception>
    public T Value
    {
        get
        {
            ResourceManager? manager = Volatile.Read(ref _manager);
            ObjectDisposedException.ThrowIf(manager is null, this);
            manager.ValidateLease(_generation);
            return (T)_generation.Value!;
        }
    }

    /// <summary>Returns the revision's ownership once without I/O, resource destruction or GPU waits.</summary>
    public void Dispose() => Interlocked.Exchange(ref _manager, null)?.ReturnLease(_generation);
}
