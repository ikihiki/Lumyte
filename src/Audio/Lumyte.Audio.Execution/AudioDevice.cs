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

    [ExecutionCommand(AllowAwaitableResult = true)]
    private Task<IAudioDevice> BeginCreateCore() => _original.BeginCreate();

    [ExecutionCommand]
    private void AttachCore(IAudioDevice device) => _original.Attach(device);

    [ExecutionCommand]
    private IAudioClip CreateClipCore(AudioFormat format, ReadOnlyMemory<float> samples) => _original.CreateClip(format, samples);

    [ExecutionCommand]
    private void SetBusGainCore(AudioBus bus, float gain) => _original.SetBusGain(bus, gain);

    [ExecutionCommand]
    private void RefreshCore() => _original.Refresh();

    [ExecutionUpdate]
    private void Update(in UpdateTick tick) => _original.Update(in tick);

    [ExecutionCommand]
    private void CloseCore() => _original.Close();
}
