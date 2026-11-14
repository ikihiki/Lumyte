using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution.Benchmark;

/// <summary>CPU dispatch contract backend with exact control counts; never represents native audio performance.</summary>
public sealed class CountingAudioDevice : IAudioDevice
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly HashSet<Voice> _voices = [];
    private int _clips;
    private bool _disposed;
    private bool _invalid;

    /// <summary>Gets the number of executed voice and bus controls (excluding periodic updates).</summary>
    public int Controls { get; private set; }

    /// <summary>Gets the number of owned clips.</summary>
    public int LiveClips => _clips;

    /// <summary>Gets the number of owned voices.</summary>
    public int LiveVoices => _voices.Count;

    /// <summary>Gets a value indicating whether ordered cleanup reached device destruction.</summary>
    public bool IsDisposed => _disposed;

    /// <inheritdoc/>
    public AudioOutputState OutputState
    {
        get
        {
            Verify();
            return _invalid ? AudioOutputState.Unavailable : AudioOutputState.Running;
        }
    }

    /// <inheritdoc/>
    public void Invalidate()
    {
        Verify();
        _invalid = true;
    }

    /// <inheritdoc/>
    public void Update()
    {
        Verify();
        foreach (Voice voice in _voices)
        {
            voice.Refresh();
        }
    }

    /// <inheritdoc/>
    public ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default)
    {
        Verify();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public IAudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples)
    {
        Verify();
        _clips++;
        return new Clip(this, format, samples.ToArray());
    }

    /// <inheritdoc/>
    public IAudioVoice CreateVoice(IAudioClip clip, AudioBus bus = AudioBus.Effects)
    {
        Verify();
        var owned = (Clip)clip;
        if (!ReferenceEquals(owned.Device, this))
        {
            throw new InvalidOperationException("Wrong owner.");
        }

        owned.Borrowers++;
        var voice = new Voice(this, owned);
        _voices.Add(voice);
        return voice;
    }

    /// <inheritdoc/>
    public void SetBusGain(AudioBus bus, float gain)
    {
        Verify();
        Controls++;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Verify();
        if (_clips != 0 || _voices.Count != 0)
        {
            throw new InvalidOperationException("Children must be destroyed before their device.");
        }

        _disposed = true;
    }

    private void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _owner)
        {
            throw new InvalidOperationException("Backend invoked off owner thread.");
        }
    }

    private sealed class Clip(CountingAudioDevice device, AudioFormat format, float[] samples) : IAudioClip
    {
        private bool _disposed;

        public IAudioDevice Device => device;

        public AudioFormat Format => format;

        public long FrameCount => samples.Length / format.Channels;

        internal int Borrowers { get; set; }

        public void Dispose()
        {
            device.Verify();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Borrowers != 0)
            {
                throw new InvalidOperationException("Voice still borrows clip.");
            }

            _disposed = true;
            device._clips--;
        }
    }

    private sealed class Voice(CountingAudioDevice device, Clip clip) : IAudioVoice
    {
        private AudioPlaybackState _requested;
        private long _position;
        private float _gain = 1;
        private bool _loop;
        private bool _disposed;

        public AudioPlaybackState State { get; private set; }

        public long FramePosition { get; private set; }

        public float Gain
        {
            get => _gain;
            set
            {
                Control();
                _gain = value;
            }
        }

        public bool Loop
        {
            get => _loop;
            set
            {
                Control();
                _loop = value;
            }
        }

        public void Play()
        {
            Control();
            _requested = AudioPlaybackState.Playing;
        }

        public void Pause()
        {
            Control();
            _requested = AudioPlaybackState.Paused;
        }

        public void Stop()
        {
            Control();
            _requested = AudioPlaybackState.Stopped;
            _position = 0;
        }

        public void Seek(long framePosition)
        {
            Control();
            ArgumentOutOfRangeException.ThrowIfNegative(framePosition);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(framePosition, clip.FrameCount);
            _position = framePosition;
        }

        public void Dispose()
        {
            device.Verify();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _disposed = true;
            clip.Borrowers--;
            device._voices.Remove(this);
        }

        internal void Refresh()
        {
            State = _requested;
            FramePosition = _position;
        }

        private void Control()
        {
            device.Verify();
            ObjectDisposedException.ThrowIf(_disposed, this);
            device.Controls++;
        }
    }
}
