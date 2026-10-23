using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution;

/// <summary>Creates a backend on the audio owner. Browser implementations retain the host synchronization context.</summary>
public interface IAudioDeviceFactory
{
    /// <summary>Creates the actual device. Completion must mean creation has finished; startup observes it even during shutdown.</summary>
    /// <returns>The owned device.</returns>
    Task<IAudioDevice> CreateAsync();
}
