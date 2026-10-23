using System.Runtime.InteropServices.JavaScript;
using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Browser;

internal sealed class WebAudioVoice : IAudioVoice
{
    private readonly WebAudioDevice _device;
    private readonly WebAudioClip _clip;
    private readonly AudioBus _bus;
    private readonly JSObject _handle;
    private AudioPlaybackState _state;
    private AudioPlaybackState _requested;
    private float _gain = 1;
    private bool _loop;
    private bool _disposed;

    internal WebAudioVoice(WebAudioDevice device, WebAudioClip clip, AudioBus bus)
    {
        (_device, _clip, _bus) = (device, clip, bus);
        _handle = WebAudioInterop.CreateVoice(device.Handle, clip.Handle, device.Gain(bus));
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
            WebAudioInterop.SetLoop(_handle, value);
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

        WebAudioInterop.Play(_handle);
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

        WebAudioInterop.Pause(_handle);
        _requested = AudioPlaybackState.Paused;
    }

    public void Stop()
    {
        Validate();
        WebAudioInterop.Stop(_handle);
        _requested = AudioPlaybackState.Stopped;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _device.Validate(false);
        WebAudioInterop.DestroyVoice(_handle);
        _handle.Dispose();
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
        WebAudioInterop.SetGain(_handle, gain);
    }

    private void Observe()
    {
        if (_requested == AudioPlaybackState.Playing && WebAudioInterop.Completed(_handle))
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
