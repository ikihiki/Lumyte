using Lumyte.Audio.Abstractions;
using Silk.NET.OpenAL;

namespace Lumyte.Audio.OpenAL;

/// <summary>Backend-owned audio device with thread-affine controls.</summary>
public sealed class OpenALDevice : IAudioDevice
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly int _maxVoices;
    private readonly HashSet<OpenALClip> _clips = [];
    private readonly HashSet<OpenALVoice> _voices = [];
    private readonly float[] _gains = [1, 1, 1];

    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly OpenALThreadContext _threadContext;
    private readonly unsafe Device* _device;
    private readonly unsafe Context* _context;

    private bool _contextDestroyed;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="OpenALDevice"/> class. Opens an OpenAL device; OpenAL Soft must be installed on the host.</summary>
    /// <param name="deviceName">Optional output device name.</param>
    /// <param name="maxVoices">Positive maximum number of live voices.</param>
    public unsafe OpenALDevice(string? deviceName = null, int maxVoices = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVoices);
        _maxVoices = maxVoices;
        _al = AL.GetApi();
        try
        {
            _alc = ALContext.GetApi();
        }
        catch
        {
            _al.Dispose();
            throw;
        }

        try
        {
            _device = _alc.OpenDevice(deviceName);
            if (_device == null)
            {
                throw new InvalidOperationException("OpenAL could not open the output device.");
            }

            _threadContext = new OpenALThreadContext(_alc, _device);
            _context = _alc.CreateContext(_device, null);
            if (_context == null)
            {
                throw new InvalidOperationException("OpenAL could not create its context.");
            }

            _threadContext.SetCurrent(_context);
            _al.DistanceModel(DistanceModel.None);
            CheckError();
        }
        catch
        {
            if (_context != null)
            {
                _threadContext?.ClearIfCurrent(_context);
                _alc.DestroyContext(_context);
            }

            if (_device != null)
            {
                _alc.CloseDevice(_device);
            }

            _al.Dispose();
            _alc.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public AudioOutputState OutputState
    {
        get
        {
            Validate();
            unsafe
            {
                if (_alc.IsExtensionPresent(_device, "ALC_EXT_disconnect"))
                {
                    const int Connected = 0x313;
                    int connected = 0;
                    _alc.GetContextProperty(_device, (GetContextInteger)Connected, 1, &connected);
                    if (connected == 0)
                    {
                        return AudioOutputState.Unavailable;
                    }
                }
            }

            return AudioOutputState.Running;
        }
    }

    internal AL Api => _al;

    /// <inheritdoc />
    public void Update()
    {
        Validate();
        foreach (OpenALVoice voice in _voices)
        {
            voice.Refresh();
        }
    }

    /// <inheritdoc />
    public ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (OutputState == AudioOutputState.Unavailable)
        {
            throw new NotSupportedException("A disconnected OpenAL device must be recreated after its children are released.");
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public IAudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples)
    {
        Validate();
        AudioValidation.Pcm(format, samples);
        var clip = new OpenALClip(this, format, samples);
        _clips.Add(clip);
        return clip;
    }

    /// <inheritdoc />
    public IAudioVoice CreateVoice(IAudioClip clip, AudioBus bus = AudioBus.Effects)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(clip);
        AudioValidation.Bus(bus, true);
        if (clip is not OpenALClip owned || !ReferenceEquals(owned.Device, this))
        {
            throw new ArgumentException("Clip belongs to another device.", nameof(clip));
        }

        owned.Validate();
        if (_voices.Count >= _maxVoices)
        {
            throw new InvalidOperationException("The voice limit has been reached.");
        }

        var voice = new OpenALVoice(this, owned, bus);
        _voices.Add(voice);
        owned.References++;
        return voice;
    }

    /// <inheritdoc />
    public void SetBusGain(AudioBus bus, float gain)
    {
        Validate();
        AudioValidation.Bus(bus);
        AudioValidation.Gain(gain);
        _gains[(int)bus] = gain;
        foreach (OpenALVoice voice in _voices)
        {
            voice.ApplyGain();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("Audio control must run on the owning thread.");
        }

        if (_clips.Count != 0 || _voices.Count != 0)
        {
            throw new InvalidOperationException("Dispose all clips and voices before the device.");
        }

        unsafe
        {
            if (!_contextDestroyed)
            {
                _threadContext.ClearIfCurrent(_context);
                _alc.DestroyContext(_context);
                _contextDestroyed = true;
            }

            if (!_alc.CloseDevice(_device))
            {
                throw new InvalidOperationException("OpenAL device close failed.");
            }
        }

        _al.Dispose();
        _alc.Dispose();
        _disposed = true;
    }

    internal void CheckError()
    {
        AudioError error = _al.GetError();
        if (error != AudioError.NoError)
        {
            throw new InvalidOperationException($"OpenAL error: {error}.");
        }
    }

    internal float Gain(AudioBus bus) => _gains[0] * _gains[(int)bus];

    internal void Remove(OpenALClip clip) => _clips.Remove(clip);

    internal void Remove(OpenALVoice voice) => _voices.Remove(voice);

    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_contextDestroyed)
        {
            throw new InvalidOperationException("Only disposal retry is allowed after context shutdown.");
        }

        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("Audio control must run on the owning thread.");
        }

        unsafe
        {
            _threadContext.SetCurrent(_context);
        }
    }
}
