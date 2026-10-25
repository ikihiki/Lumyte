using System.Runtime.InteropServices.JavaScript;
using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Browser;

/// <summary>Backend-owned audio device with thread-affine controls.</summary>
public sealed class WebAudioDevice : IAudioDevice
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly int _maxVoices;
    private readonly HashSet<WebAudioClip> _clips = [];
    private readonly HashSet<WebAudioVoice> _voices = [];
    private readonly float[] _gains = [1, 1, 1];

    private readonly JSObject _handle;

    private bool _disposed;

    private WebAudioDevice(JSObject handle, int maxVoices)
    {
        _handle = handle;
        _maxVoices = maxVoices;
    }

    /// <inheritdoc />
    public AudioOutputState OutputState
    {
        get
        {
            Validate();
            return (AudioOutputState)WebAudioInterop.OutputState(_handle);
        }
    }

    internal JSObject Handle => _handle;

    /// <summary>Imports the Web Audio module and creates a suspended-capable AudioContext.</summary>
    /// <param name="moduleUrl">Absolute URL or URL relative to the browser location.</param>
    /// <param name="maxVoices">Positive maximum number of live voices.</param>
    /// <param name="cancellationToken">Cancels module import.</param>
    /// <returns>The owned browser device.</returns>
    public static async Task<WebAudioDevice> CreateAsync(string moduleUrl, int maxVoices = 64, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleUrl);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVoices);
        using JSObject location = JSHost.GlobalThis.GetPropertyAsJSObject("location") ?? throw new InvalidOperationException("Browser location is unavailable.");
        var url = new Uri(new Uri(location.GetPropertyAsString("href") ?? throw new InvalidOperationException("Browser URL is unavailable.")), moduleUrl);
        await JSHost.ImportAsync("Lumyte.Audio.Browser", url.AbsoluteUri, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(WebAudioInterop.CreateDevice(), maxVoices);
    }

    /// <inheritdoc />
    public void Update()
    {
        Validate();
        foreach (WebAudioVoice voice in _voices)
        {
            voice.Refresh();
        }
    }

    /// <inheritdoc />
    public ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(WebAudioInterop.ResumeAsync(_handle).WaitAsync(cancellationToken));
    }

    /// <inheritdoc />
    public IAudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples)
    {
        Validate();
        AudioValidation.Pcm(format, samples);
        var clip = new WebAudioClip(this, format, samples);
        _clips.Add(clip);
        return clip;
    }

    /// <inheritdoc />
    public IAudioVoice CreateVoice(IAudioClip clip, AudioBus bus = AudioBus.Effects)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(clip);
        AudioValidation.Bus(bus, true);
        if (clip is not WebAudioClip owned || !ReferenceEquals(owned.Device, this))
        {
            throw new ArgumentException("Clip belongs to another device.", nameof(clip));
        }

        owned.Validate();
        if (_voices.Count >= _maxVoices)
        {
            throw new InvalidOperationException("The voice limit has been reached.");
        }

        var voice = new WebAudioVoice(this, owned, bus);
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
        foreach (WebAudioVoice voice in _voices)
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

        Validate();
        if (_clips.Count != 0 || _voices.Count != 0)
        {
            throw new InvalidOperationException("Dispose all clips and voices before the device.");
        }

        WebAudioInterop.DestroyDevice(_handle);
        _handle.Dispose();
        _disposed = true;
    }

    internal float Gain(AudioBus bus) => _gains[0] * _gains[(int)bus];

    internal void Remove(WebAudioClip clip) => _clips.Remove(clip);

    internal void Remove(WebAudioVoice voice) => _voices.Remove(voice);

    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("Audio control must run on the owning thread.");
        }
    }
}
