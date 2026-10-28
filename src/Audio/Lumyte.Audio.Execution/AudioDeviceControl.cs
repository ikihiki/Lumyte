using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>A one-reference device sender with owner-driven lifecycle and updates.</summary>
internal readonly partial struct AudioDeviceControl
{
    private readonly AudioDeviceState _original;

    internal AudioDeviceControl(AudioDeviceState original)
    {
        _original = original;
    }

    internal IAudioDeviceService Devices => _original;

    internal Task RecoveryCompletion => _original.RecoveryCompletion;

    internal Task<IAudioDevice>? PendingCreation => _original.PendingCreation;

    [ExecutionCommand(AllowAwaitableResult = true)]
    private Task<IAudioDevice> BeginCreateCore() => _original.BeginCreate(false);

    [ExecutionCommand]
    private AudioDeviceSnapshot? BeginRecoveryCore() => _original.BeginRecovery();

    [ExecutionCommand]
    private AudioDeviceSnapshot CompleteCreateCore() => _original.CompleteCreate();

    [ExecutionCommand]
    private AudioDeviceLease AcquireLeaseCore() => _original.AcquireLease();

    [ExecutionCommand]
    private int RetireCore() => _original.Retire();

    [ExecutionCommand]
    private void BeginCloseCore() => _original.BeginClose();

    [ExecutionUpdate]
    private void Update(in UpdateTick tick) => _original.Update(in tick);

    [ExecutionCommand]
    private void CloseCore() => _original.Close();
}
