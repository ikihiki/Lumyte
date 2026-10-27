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

    /// <summary>Gets an owner-dispatched device bound to this exact generation; loss and lease disposal reject access.</summary>
    public AudioDevice Device
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _returned) != 0, this);
            _service.VerifyUsable(_entry);
            return new(this);
        }
    }

    internal int OwnerThreadId => _service.OwnerThreadId;

    /// <summary>Returns the lease once; native child destruction must already be complete.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _returned, 1) == 0)
        {
            _service.Release(_entry);
        }
    }

    internal AudioClip CreateClip(AudioFormat format, ReadOnlyMemory<float> samples)
        => new(_service.CreateClipForLease(this, format, samples));

    internal (AudioClip Clip, Exception? ValidationError) CreateClipWithValidation(AudioFormat format, ReadOnlyMemory<float> samples)
    {
        IAudioClip clip = _service.CreateClipForLease(this, format, samples);
        try
        {
            _service.ValidateCreation();
            return (new(clip), null);
        }
        catch (Exception error)
        {
            // Transfer the candidate even when validation fails, so its owner can
            // retain failed native cleanup and the exact generation for retry.
            return (new(clip), error);
        }
    }

    internal void SetBusGain(AudioBus bus, float gain) => _service.SetBusGain(this, bus, gain);

    internal void Refresh()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _returned) != 0, this);
        _service.Refresh(_entry);
    }

    internal IAudioDevice GetDevice(AudioDeviceState service)
    {
        if (!ReferenceEquals(_service, service))
        {
            throw new ArgumentException("Audio lease belongs to another service.");
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _returned) != 0, this);
        _service.VerifyUsable(_entry);
        return _entry.Device;
    }
}
