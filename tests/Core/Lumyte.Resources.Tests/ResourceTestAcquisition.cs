namespace Lumyte.Resources.Tests;

/// <summary>Acquires short-lived test leases through the public reference contract.</summary>
internal static class ResourceTestAcquisition
{
    /// <summary>Obtains a reference, pins its current value and returns the temporary reference.</summary>
    /// <typeparam name="T">The resource output type.</typeparam>
    /// <param name="manager">The owning manager.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="cancellationToken">Cancels reference acquisition.</param>
    /// <returns>A lease owned by the test.</returns>
    internal static async ValueTask<ResourceLease<T>> AcquireLeaseForTestAsync<T>(this ResourceManager manager, ResourceKey<T> key, CancellationToken cancellationToken = default)
        where T : class
    {
        using ResourceReference<T> reference = await manager.AcquireAsync(key, cancellationToken);
        return reference.Acquire();
    }

    /// <summary>Resolves a handle through a temporary owning reference.</summary>
    /// <typeparam name="T">The resource output type.</typeparam>
    /// <param name="manager">The owning manager.</param>
    /// <param name="handle">The logical handle.</param>
    /// <param name="lease">The retained value, or null.</param>
    /// <returns>Whether a current value could be retained.</returns>
    internal static bool TryAcquireLeaseForTest<T>(this ResourceManager manager, ResourceHandle<T> handle, out ResourceLease<T>? lease)
        where T : class
    {
        lease = null;
        if (!manager.TryAcquire(handle, out ResourceReference<T>? reference))
        {
            return false;
        }

        using (reference)
        {
            return reference!.TryAcquire(out lease);
        }
    }
}
