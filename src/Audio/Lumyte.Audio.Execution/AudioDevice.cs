using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Typed commands operating on real backend instances on their creation thread.</summary>
public sealed partial class AudioDevice
{
    private readonly IAudioDeviceFactory _factory;
    private IAudioDevice? _device;
    private Task<IAudioDevice>? _pending;
    private int _ownerThreadId;

    internal AudioDevice(IAudioDeviceFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Gets the thread on which backend creation was started.</summary>
    public int OwnerThreadId => Volatile.Read(ref _ownerThreadId);

    internal IAudioDevice Backend => _device ?? throw new InvalidOperationException("Audio is not initialized.");

    [ExecutionCommand(AllowAwaitableResult = true)]
    private Task<IAudioDevice> BeginCreate()
    {
        Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
        return _pending = _factory.CreateAsync();
    }

    [ExecutionCommand]
    private void Attach(IAudioDevice device)
    {
        _device = device;
        _pending = null;
    }

    [ExecutionCommand]
    private AudioClip CreateClip(AudioFormat format, ReadOnlyMemory<float> samples) => new(Backend, Backend.CreateClip(format, samples.Span));

    [ExecutionCommand]
    private void SetBusGain(AudioBus bus, float gain) => Backend.SetBusGain(bus, gain);

    [ExecutionCommand]
    private void Refresh() => Backend.Update();

    [ExecutionUpdate]
    private void Update(in UpdateTick tick) => Backend.Update();

    [ExecutionCommand]
    private void Close()
    {
        IAudioDevice? device = _device ?? (_pending?.IsCompletedSuccessfully == true ? _pending.Result : null);
        device?.Dispose();
        _device = null;
        _pending = null;
    }
}
