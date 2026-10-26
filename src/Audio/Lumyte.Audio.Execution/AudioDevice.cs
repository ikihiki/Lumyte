using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>A one-reference device sender with owner-driven lifecycle and updates.</summary>
public readonly partial struct AudioDevice
{
    private readonly AudioDeviceState _original;

    internal AudioDevice(AudioDeviceState original)
    {
        _original = original;
    }

    /// <summary>Gets the thread which started backend creation.</summary>
    public int OwnerThreadId => _original.OwnerThreadId;

    internal IAudioDeviceService Devices => _original;

    internal Task RecoveryCompletion => _original.RecoveryCompletion;

    internal Task<IAudioDevice>? PendingCreation => _original.PendingCreation;

    [ExecutionCommand(AllowAwaitableResult = true)]
    private Task<IAudioDevice> BeginCreateCore() => _original.BeginCreate(false);

    [ExecutionCommand]
    private void BeginRecoveryCore() => _original.BeginCreate(true);

    [ExecutionCommand]
    private AudioDeviceSnapshot CompleteCreateCore() => _original.CompleteCreate();

    [ExecutionCommand]
    private AudioDeviceLease AcquireLeaseCore() => _original.AcquireLease();

    [ExecutionCommand]
    private int RetireCore() => _original.Retire();

    [ExecutionCommand]
    private void BeginCloseCore() => _original.BeginClose();

    [ExecutionCommand]
    private IAudioClip CreateClipCore(AudioFormat format, ReadOnlyMemory<float> samples) => _original.CreateClip(format, samples);

    [ExecutionCommand]
    private IAudioClip CreateClipForLeaseCore(AudioDeviceLease lease, AudioFormat format, ReadOnlyMemory<float> samples) => _original.CreateClipForLease(lease, format, samples);

    [ExecutionCommand]
    private (AudioDeviceLease Lease, IAudioClip Clip, Exception? ValidationError) CreateClipWithLeaseCore(AudioFormat format, ReadOnlyMemory<float> samples)
        => _original.CreateClipWithLease(format, samples);

    [ExecutionCommand]
    private void SetBusGainCore(AudioBus bus, float gain) => _original.SetBusGain(bus, gain);

    [ExecutionCommand]
    private void RefreshCore() => _original.Refresh();

    [ExecutionUpdate]
    private void Update(in UpdateTick tick) => _original.Update(in tick);

    [ExecutionCommand]
    private void CloseCore() => _original.Close();
}
