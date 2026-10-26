using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution;

/// <summary>Retains one exact device generation until its native children have been released.</summary>
public sealed class AudioDeviceLease : IDisposable
{
    private readonly AudioDeviceState _service;
    private readonly AudioDeviceEntry _entry;
    private int _returned;

    internal AudioDeviceLease(AudioDeviceState service, AudioDeviceEntry entry, AudioDeviceSnapshot state)
    {
        (_service, _entry, State) = (service, entry, state);
    }

    /// <summary>Gets the Ready snapshot captured at acquisition.</summary>
    public AudioDeviceSnapshot State { get; }

    /// <summary>Gets a value indicating whether the retained generation has not been observed lost.</summary>
    public bool IsUsable => Volatile.Read(ref _returned) == 0 && _service.IsUsable(_entry);

    /// <summary>Gets the original backend; native operations still require its owning thread.</summary>
    public IAudioDevice Device
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _returned) != 0, this);
            _service.VerifyUsable(_entry);
            return _entry.Device;
        }
    }

    /// <summary>Returns the lease once; native child destruction must already be complete.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _returned, 1) == 0)
        {
            _service.Release(_entry);
        }
    }

    internal IAudioDevice GetDevice(AudioDeviceState service)
    {
        if (!ReferenceEquals(_service, service))
        {
            throw new ArgumentException("Audio lease belongs to another service.");
        }

        return Device;
    }
}
