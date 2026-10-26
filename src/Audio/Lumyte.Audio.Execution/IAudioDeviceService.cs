namespace Lumyte.Audio.Execution;

/// <summary>Retains audio device generations and recreates lost devices on their owner.</summary>
public interface IAudioDeviceService
{
    /// <summary>Notifies availability changes without starting recovery automatically.</summary>
    event EventHandler? StateChanged;

    /// <summary>Gets the immutable service state from any thread.</summary>
    AudioDeviceSnapshot State { get; }

    /// <summary>Acquires the current usable generation on its owner.</summary>
    /// <param name="cancellationToken">Cancels before acquisition executes.</param>
    /// <returns>A lease retaining the exact device generation.</returns>
    ValueTask<AudioDeviceLease> AcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>Recreates a lost or failed device; concurrent callers share creation.</summary>
    /// <param name="cancellationToken">Cancels this caller's wait without abandoning creation.</param>
    /// <returns>The new available generation.</returns>
    ValueTask<AudioDeviceSnapshot> RecoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Disposes unused old devices on their owner, retaining failed cleanup for retry.</summary>
    /// <param name="cancellationToken">Cancels before retirement executes.</param>
    /// <returns>The number of retired devices.</returns>
    ValueTask<int> RetireAsync(CancellationToken cancellationToken = default);
}
