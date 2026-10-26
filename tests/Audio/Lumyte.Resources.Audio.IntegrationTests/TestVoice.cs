namespace Lumyte.Resources.Audio.IntegrationTests;

internal sealed class TestVoice(TestDevice device, TestClip clip) : IAudioVoice
{
    private AudioPlaybackState _state;
    private AudioPlaybackState _requested;
    private long _position;
    private long _requestedPosition;
    private bool _disposed;

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
                return _position;
            }
        }
    }

    public float Gain { get; set; } = 1;

    public bool Loop { get; set; }

    internal TestDevice Device => device;

    internal TestClip Clip => clip;

    public void Play()
    {
        Validate();
        if (_requested == AudioPlaybackState.Playing)
        {
            return;
        }

        if (_requested == AudioPlaybackState.Completed || (Loop && _requestedPosition == clip.FrameCount))
        {
            _requestedPosition = 0;
        }

        _requested = _requestedPosition == clip.FrameCount ? AudioPlaybackState.Completed : AudioPlaybackState.Playing;
    }

    public void Pause()
    {
        Validate();
        if (_requested == AudioPlaybackState.Playing)
        {
            _requested = AudioPlaybackState.Paused;
        }
    }

    public void Stop()
    {
        Validate();
        _requestedPosition = 0;
        _requested = AudioPlaybackState.Stopped;
    }

    public void Seek(long framePosition)
    {
        Validate();
        if (framePosition < 0 || framePosition > clip.FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(framePosition));
        }

        _requestedPosition = Loop && framePosition == clip.FrameCount ? 0 : framePosition;
        if (_requested == AudioPlaybackState.Completed && _requestedPosition < clip.FrameCount)
        {
            _requested = AudioPlaybackState.Stopped;
        }
        else if (_requested == AudioPlaybackState.Playing && _requestedPosition == clip.FrameCount)
        {
            _requested = AudioPlaybackState.Completed;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        device.VerifyVoiceRelease();
        clip.Borrowers--;
        device.ReturnVoice(this);
        lock (this)
        {
            _disposed = true;
        }
    }

    internal void Refresh()
    {
        lock (this)
        {
            _state = _requested;
            _position = _requestedPosition;
        }
    }

    private void Validate()
    {
        device.VerifyAvailable();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
