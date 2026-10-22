namespace Lumyte.Resources;

/// <summary>Retains a consistent publication view for resolving multiple resource handles.</summary>
/// <remarks>Use and disposal of the same scope must not overlap.</remarks>
public sealed class ResourceReadScope : IDisposable
{
    private readonly IResourceReadView _view;
    private Action? _release;

    internal ResourceReadScope(ulong publicationVersion, IResourceReadView view, Action release)
    {
        (PublicationVersion, _view, _release) = (publicationVersion, view, release);
    }

    /// <summary>Gets the publication version captured when this scope was opened.</summary>
    public ulong PublicationVersion { get; }

    /// <summary>Attempts to retain a revision from the captured view without I/O or pipeline execution.</summary>
    /// <typeparam name="T">The handle's exact resource output type.</typeparam>
    /// <param name="handle">The nonowning handle to resolve.</param>
    /// <param name="lease">The retained revision, or <see langword="null"/> if the handle is unavailable.</param>
    /// <returns>Whether a usable revision was retained.</returns>
    /// <exception cref="ObjectDisposedException">This scope has been returned.</exception>
    public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease)
        where T : class
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
        return _view.TryAcquire(handle, out lease);
    }

    /// <summary>Returns the view's protection once without invalidating leases already issued from it.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
