using Lumyte.Audio.Abstractions;
using Silk.NET.OpenAL;

namespace Lumyte.Audio.OpenAL;

internal sealed class OpenALVoice : IAudioVoice
{
    private readonly OpenALDevice _device;
    private readonly OpenALClip _clip;
    private readonly AudioBus _bus;
    private readonly uint _handle;
    private AudioPlaybackState _state;
    private AudioPlaybackState _requested;
    private float _gain = 1;
    private bool _loop;
    private bool _disposed;

    internal OpenALVoice(OpenALDevice device, OpenALClip clip, AudioBus bus)
    {
        (_device, _clip, _bus) = (device, clip, bus);
        _handle = device.Api.GenSource();
        device.CheckError();
        try
        {
            device.Api.SetSourceProperty(_handle, SourceInteger.Buffer, (int)clip.Handle);
            device.Api.SetSourceProperty(_handle, SourceBoolean.SourceRelative, true);
            ApplyGain();
            device.CheckError();
        }
        catch
        {
            device.Api.DeleteSource(_handle);
            device.CheckError();
            throw;
        }
    }

    public AudioPlaybackState State
    {
        get
        {
            lock (this)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _state;
            }
        }
    }

    public float Gain
    {
        get
        {
            lock (this)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _gain;
            }
        }

        set
        {
            Validate();
            AudioValidation.Gain(value);
            lock (this)
            {
                _gain = value;
            }

            ApplyGain();
        }
    }

    public bool Loop
    {
        get
        {
            lock (this)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _loop;
            }
        }

        set
        {
            Validate();
            _device.Api.SetSourceProperty(_handle, SourceBoolean.Looping, value);
            _device.CheckError();
            lock (this)
            {
                _loop = value;
            }
        }
    }

    public void Play()
    {
        Validate();
        Observe();
        if (_requested == AudioPlaybackState.Playing)
        {
            return;
        }

        _device.Api.SourcePlay(_handle);
        _device.CheckError();
        _requested = AudioPlaybackState.Playing;
    }

    public void Pause()
    {
        Validate();
        Observe();
        if (_requested != AudioPlaybackState.Playing)
        {
            return;
        }

        _device.Api.SourcePause(_handle);
        _device.CheckError();
        _requested = AudioPlaybackState.Paused;
    }

    public void Stop()
    {
        Validate();
        _device.Api.SourceStop(_handle);
        _device.Api.SourceRewind(_handle);
        _device.CheckError();
        _requested = AudioPlaybackState.Stopped;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Validate();
        _device.Api.SourceStop(_handle);
        _device.CheckError();
        _device.Api.DeleteSource(_handle);
        _device.CheckError();
        lock (this)
        {
            _disposed = true;
        }

        _clip.References--;
        _device.Remove(this);
    }

    internal void Refresh()
    {
        Observe();
        lock (this)
        {
            _state = _requested;
        }
    }

    internal void ApplyGain()
    {
        float gain = _gain * _device.Gain(_bus);
        _device.Api.SetSourceProperty(_handle, SourceFloat.Gain, gain);
        _device.CheckError();
    }

    private void Observe()
    {
        _device.Api.GetSourceProperty(_handle, GetSourceInteger.SourceState, out int nativeState);
        _device.CheckError();
        if (_requested == AudioPlaybackState.Playing && nativeState == (int)SourceState.Stopped)
        {
            _requested = AudioPlaybackState.Completed;
        }
    }

    private void Validate()
    {
        _device.Validate();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
