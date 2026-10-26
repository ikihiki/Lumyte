using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.OpenAL;

internal sealed class OpenALVoice : IAudioVoice
{
    private readonly OpenALDevice _device;
    private readonly OpenALClip _clip;
    private readonly AudioBus _bus;
    private readonly uint _handle;
    private AudioPlaybackState _state;
    private AudioPlaybackState _requested;
    private long _framePosition;
    private long _position;
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

    public long FramePosition
    {
        get
        {
            lock (this)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _framePosition;
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

        if (_requested == AudioPlaybackState.Completed || (_loop && _position == _clip.FrameCount))
        {
            _position = 0;
        }

        if (_position == _clip.FrameCount)
        {
            _requested = AudioPlaybackState.Completed;
            return;
        }

        _device.Api.SetSourceProperty(_handle, SourceInteger.SampleOffset, checked((int)_position));
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
        Observe();
        if (_requested == AudioPlaybackState.Playing)
        {
            _requested = AudioPlaybackState.Paused;
        }
    }

    public void Stop()
    {
        Validate();
        _device.Api.SourceStop(_handle);
        _device.Api.SourceRewind(_handle);
        _device.CheckError();
        _requested = AudioPlaybackState.Stopped;
        _position = 0;
    }

    public void Seek(long framePosition)
    {
        Validate();
        if (framePosition < 0 || framePosition > _clip.FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(framePosition));
        }

        Observe();
        if (_loop && framePosition == _clip.FrameCount)
        {
            framePosition = 0;
        }

        if (framePosition == _clip.FrameCount)
        {
            // AL_SAMPLE_OFFSET cannot represent the position after the final frame.
            _device.Api.SourceStop(_handle);
            _device.Api.SourceRewind(_handle);
            _device.CheckError();
            if (_requested == AudioPlaybackState.Playing)
            {
                _requested = AudioPlaybackState.Completed;
            }
        }
        else
        {
            if (_requested is AudioPlaybackState.Stopped or AudioPlaybackState.Completed)
            {
                _device.Api.SourceRewind(_handle);
            }

            _device.Api.SetSourceProperty(_handle, SourceInteger.SampleOffset, checked((int)framePosition));
            _device.CheckError();
            if (_requested == AudioPlaybackState.Playing)
            {
                // The native mixer can finish between Observe and the offset update.
                _device.Api.GetSourceProperty(_handle, GetSourceInteger.SourceState, out int nativeState);
                _device.CheckError();
                if (nativeState == (int)SourceState.Stopped)
                {
                    _device.Api.SetSourceProperty(_handle, SourceInteger.SampleOffset, checked((int)framePosition));
                    _device.Api.SourcePlay(_handle);
                    _device.CheckError();
                }
            }

            if (_requested == AudioPlaybackState.Completed)
            {
                _requested = AudioPlaybackState.Stopped;
            }
        }

        _position = framePosition;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_device.PrepareCleanup())
        {
            _device.Api.DeleteSource(_handle);
            _device.CheckError();
        }

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
            _framePosition = _position;
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
        if (_requested != AudioPlaybackState.Playing)
        {
            return;
        }

        // Read the cursor first so a concurrent native completion cannot publish its reset cursor as playing.
        _device.Api.GetSourceProperty(_handle, GetSourceInteger.SampleOffset, out int position);
        _device.Api.GetSourceProperty(_handle, GetSourceInteger.SourceState, out int nativeState);
        _device.CheckError();
        if (nativeState == (int)SourceState.Stopped)
        {
            _requested = AudioPlaybackState.Completed;
            _position = _clip.FrameCount;
        }
        else
        {
            _position = position;
        }
    }

    private void Validate()
    {
        _device.Validate();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
