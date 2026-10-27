using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Lumyte.Resources.Graphics.Tests;

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
        using ResourceReference<T> reference = await manager.RetainAsync(key, cancellationToken);
        return reference.Pin();
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
        if (!manager.TryRetain(handle, out ResourceReference<T>? reference))
        {
            return false;
        }

        using (reference)
        {
            return reference!.TryPin(out lease);
        }
    }

    /// <summary>Runs the internal reload debug command for graph regression tests.</summary>
    /// <param name="manager">The manager under test.</param>
    /// <param name="sources">The changed input identifiers.</param>
    /// <param name="cancellationToken">Cancels the debug command.</param>
    /// <returns>The reload result.</returns>
    internal static ValueTask<ResourceReloadResult> ReloadForTestAsync(this ResourceManager manager, IReadOnlyCollection<ResourceId> sources, CancellationToken cancellationToken = default)
        => InvokeReload(manager, "ReloadAsync", [sources, cancellationToken]);

    /// <summary>Runs the internal tracked-input rescan debug command.</summary>
    /// <param name="manager">The manager under test.</param>
    /// <param name="cancellationToken">Cancels the debug command.</param>
    /// <returns>The reload result.</returns>
    internal static ValueTask<ResourceReloadResult> ReloadAllForTestAsync(this ResourceManager manager, CancellationToken cancellationToken = default)
        => InvokeReload(manager, "ReloadAllAsync", [cancellationToken]);

    private static ValueTask<ResourceReloadResult> InvokeReload(ResourceManager manager, string method, object[] arguments)
    {
        try
        {
            return (ValueTask<ResourceReloadResult>)typeof(ResourceManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, arguments)!;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}
