using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution;

internal sealed class AudioDeviceEntry(IAudioDevice device, ulong epoch)
{
    internal IAudioDevice Device { get; } = device;

    internal ulong Epoch { get; } = epoch;

    internal ulong ReadyVersion { get; set; }

    internal int Leases { get; set; }

    internal bool Lost { get; set; }

    internal bool Disposed { get; set; }
}
