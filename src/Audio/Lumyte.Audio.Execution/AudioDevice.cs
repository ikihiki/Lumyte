using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Sends operations to the exact device generation retained by a service lease.</summary>
/// <remarks>Keep the lease until all clips and voices have been destroyed. Copies do not retain it.</remarks>
public readonly partial struct AudioDevice
{
    private readonly AudioDeviceLease _lease;

    internal AudioDevice(AudioDeviceLease lease)
    {
        _lease = lease;
    }

    /// <summary>Gets the owning thread of this device generation.</summary>
    public int OwnerThreadId => _lease.OwnerThreadId;

    [ExecutionCommand]
    private AudioClip CreateClipCore(AudioFormat format, ReadOnlyMemory<float> samples)
        => _lease.CreateClip(format, samples);

    [ExecutionCommand]
    private (AudioClip Clip, Exception? ValidationError) CreateClipWithValidationCore(AudioFormat format, ReadOnlyMemory<float> samples)
        => _lease.CreateClipWithValidation(format, samples);

    [ExecutionCommand]
    private void SetBusGainCore(AudioBus bus, float gain) => _lease.SetBusGain(bus, gain);

    [ExecutionCommand]
    private void RefreshCore() => _lease.Refresh();
}
