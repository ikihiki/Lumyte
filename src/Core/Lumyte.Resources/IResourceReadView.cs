namespace Lumyte.Resources;

/// <summary>Resolves handles against one manager-owned publication view.</summary>
internal interface IResourceReadView
{
    /// <summary>Attempts to retain a usable revision from this view.</summary>
    /// <typeparam name="T">The handle's exact resource output type.</typeparam>
    /// <param name="handle">The handle to validate and resolve.</param>
    /// <param name="lease">The retained revision, or <see langword="null"/> if resolution fails.</param>
    /// <returns>Whether the view issued a usable lease.</returns>
    bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease)
        where T : class;
}
