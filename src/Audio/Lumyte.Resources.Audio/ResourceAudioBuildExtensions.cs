using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Records the exact audio device generation used by a resource stage.</summary>
public static class ResourceAudioBuildExtensions
{
    /// <summary>Acquires the current audio generation and transfers its lease to the build context.</summary>
    /// <param name="context">The current resource build.</param>
    /// <param name="service">The same device service registered as the runtime dependency.</param>
    /// <param name="dependencyId">The registered audio runtime dependency identifier.</param>
    /// <param name="lifetime">Whether the device is retained for construction or the resource lifetime.</param>
    /// <param name="cancellationToken">Cancels before a lease is acquired.</param>
    /// <returns>The borrowed lease; the context returns it after native child cleanup succeeds.</returns>
    public static async ValueTask<AudioDeviceLease> UseAudioAsync(this ResourceBuildContext context, IAudioDeviceService service, string dependencyId = "Audio", ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyId);
        AudioDeviceLease lease = await service.AcquireAsync(cancellationToken);
        try
        {
            context.TrackRuntimeDependency(dependencyId, lease.State.Version, lifetime, service);
            context.RegisterCleanup(
                () =>
                {
                    lease.Dispose();
                    return ValueTask.CompletedTask;
                },
                lifetime);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
