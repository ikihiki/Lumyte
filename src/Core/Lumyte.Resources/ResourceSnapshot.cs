using System.Diagnostics.CodeAnalysis;

namespace Lumyte.Resources;

/// <summary>Retains a consistent publication view for resolving multiple resource handles.</summary>
/// <remarks>Use and disposal of the same snapshot must not overlap.</remarks>
public sealed class ResourceSnapshot : IDisposable
{
    private readonly Dictionary<ResourceNode, ResourceGeneration> _map;
    private ResourceManager? _manager;

    internal ResourceSnapshot(ResourceManager manager, ulong publicationVersion, Dictionary<ResourceNode, ResourceGeneration> map)
    {
        (_manager, PublicationVersion, _map) = (manager, publicationVersion, map);
    }

    /// <summary>Gets the publication version captured by this snapshot.</summary>
    public ulong PublicationVersion { get; }

    /// <summary>Attempts to retain a revision from the captured view without I/O or pipeline execution.</summary>
    /// <typeparam name="T">The handle's exact resource output type.</typeparam>
    /// <param name="handle">The nonowning handle to resolve.</param>
    /// <param name="lease">The retained revision, or <see langword="null"/> if the handle or runtime is unavailable.</param>
    /// <returns>Whether a usable revision was retained.</returns>
    /// <exception cref="ObjectDisposedException">This snapshot has been returned.</exception>
    public bool TryAcquire<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceLease<T>? lease)
        where T : notnull
    {
        ResourceManager? manager = Volatile.Read(ref _manager);
        ObjectDisposedException.ThrowIf(manager is null, this);
        return manager.TryAcquireFromView(handle, _map, out lease);
    }

    /// <summary>Pins a value and revision from the captured publication without I/O or pipeline execution.</summary>
    /// <typeparam name="T">The handle's exact resource output type.</typeparam>
    /// <param name="handle">The nonowning handle to resolve.</param>
    /// <returns>An independently owned lease of the captured revision.</returns>
    /// <exception cref="ObjectDisposedException">This snapshot has been returned.</exception>
    /// <exception cref="InvalidOperationException">The handle has no usable value in the captured publication.</exception>
    /// <remarks>Fixes the resource generation, not a GC address. This does not copy the value or prevent runtime invalidation.</remarks>
    public ResourceLease<T> Pin<T>(ResourceHandle<T> handle)
        where T : notnull
        => TryAcquire(handle, out ResourceLease<T>? lease)
            ? lease
            : throw new InvalidOperationException("The handle has no usable value in the captured publication.");

    /// <summary>Attempts to retain a revision from the captured view without I/O or pipeline execution.</summary>
    /// <typeparam name="T">The handle's exact resource output type.</typeparam>
    /// <param name="handle">The nonowning handle to resolve.</param>
    /// <param name="lease">The retained revision, or null if the handle or runtime is unavailable.</param>
    /// <returns>Whether a usable revision was retained.</returns>
    /// <exception cref="ObjectDisposedException">This snapshot has been returned.</exception>
    /// <remarks>Fixes the resource generation, not a GC address. This does not copy the value or prevent runtime invalidation.</remarks>
    public bool TryPin<T>(ResourceHandle<T> handle, [NotNullWhen(true)] out ResourceLease<T>? lease)
        where T : notnull
        => TryAcquire(handle, out lease);

    /// <summary>Returns the view's protection once without invalidating leases already issued from it.</summary>
    public void Dispose() => Interlocked.Exchange(ref _manager, null)?.ReturnReadView(_map);
}
