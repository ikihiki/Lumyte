using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Bootstrap state holding actual device creation and shutdown.</summary>
internal sealed class AudioDeviceState
{
    private readonly IAudioDeviceFactory _factory;
    private IAudioDevice? _device;
    private Task<IAudioDevice>? _pending;
    private int _ownerThreadId;

    internal AudioDeviceState(IAudioDeviceFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Gets the thread on which backend creation was started.</summary>
    public int OwnerThreadId => Volatile.Read(ref _ownerThreadId);

    internal IAudioDevice Backend => _device ?? throw new InvalidOperationException("Audio is not initialized.");

    internal Task<IAudioDevice> BeginCreate()
    {
        Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
        return _pending = _factory.CreateAsync();
    }

    internal void Attach(IAudioDevice device)
    {
        _device = device;
        _pending = null;
    }

    internal IAudioClip CreateClip(AudioFormat format, ReadOnlyMemory<float> samples) => Backend.CreateClip(format, samples.Span);

    internal void SetBusGain(AudioBus bus, float gain) => Backend.SetBusGain(bus, gain);

    internal void Refresh() => Backend.Update();

    internal void Update(in UpdateTick tick) => Backend.Update();

    internal void Close()
    {
        IAudioDevice? device = _device ?? (_pending?.IsCompletedSuccessfully == true ? _pending.Result : null);
        device?.Dispose();
        _device = null;
        _pending = null;
    }
}
