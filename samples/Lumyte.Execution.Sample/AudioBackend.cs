namespace Lumyte.Execution.Sample;

/// <summary>Declares audio-like operations which all require their creation thread.</summary>
public sealed class AudioBackend
{
    private readonly HashSet<AudioVoiceData> _voices = [];
    private int _threadId;
    private int _updates;
    private bool _initialized;

    /// <summary>Gets the owner thread which performed initialization.</summary>
    public int ThreadId => Volatile.Read(ref _threadId);

    /// <summary>Gets the directly driven update count.</summary>
    public int UpdateCount => Volatile.Read(ref _updates);

    internal void PlayDirect(AudioVoiceData voice) => Play(voice);

    internal void Initialize()
    {
        _threadId = Environment.CurrentManagedThreadId;
        _initialized = true;
    }

    internal AudioVoiceData CreateVoice(int clip)
    {
        VerifyOwner();
        var voice = new AudioVoiceData(this, clip);
        _voices.Add(voice);
        return voice;
    }

    internal void Play(AudioVoiceData voice)
    {
        VerifyVoice(voice);
        voice.Playing = true;
    }

    internal void Stop(AudioVoiceData voice)
    {
        VerifyVoice(voice);
        voice.Playing = false;
    }

    internal void Destroy(AudioVoiceData voice)
    {
        VerifyVoice(voice);
        voice.Playing = false;
        voice.Destroyed = true;
        _voices.Remove(voice);
    }

    internal void Close()
    {
        VerifyOwner();
        foreach (AudioVoiceData voice in _voices)
        {
            voice.Destroyed = true;
            voice.Playing = false;
        }

        _voices.Clear();
        _initialized = false;
    }

    internal void UpdateControl(in UpdateTick tick)
    {
        VerifyOwner();
        _updates++;
    }

    private void VerifyOwner()
    {
        if (!_initialized || _threadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Audio operations require the device creation thread.");
        }
    }

    private void VerifyVoice(AudioVoiceData voice)
    {
        VerifyOwner();
        if (voice.Owner != this || voice.Destroyed || !_voices.Contains(voice))
        {
            throw new InvalidOperationException("Voice belongs to another device or has been destroyed.");
        }
    }
}
