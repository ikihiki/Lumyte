namespace Lumyte.Execution.Sample;

/// <summary>Declares audio-like operations which all require their creation thread.</summary>
public sealed partial class AudioState
{
    private readonly HashSet<AudioVoice> _voices = [];
    private int _threadId;
    private int _updates;
    private bool _initialized;

    /// <summary>Gets the owner thread which performed initialization.</summary>
    public int ThreadId => Volatile.Read(ref _threadId);

    /// <summary>Gets the directly driven update count.</summary>
    public int UpdateCount => Volatile.Read(ref _updates);

    internal void PlayDirect(AudioVoice voice) => Play(voice);

    [ExecutionCommand]
    private void Initialize()
    {
        _threadId = Environment.CurrentManagedThreadId;
        _initialized = true;
    }

    [ExecutionCommand]
    private AudioVoice CreateVoice(int clip)
    {
        VerifyOwner();
        var voice = new AudioVoice(this, clip);
        _voices.Add(voice);
        return voice;
    }

    [ExecutionCommand(Sender = "Voice")]
    private void Play([ExecutionTarget] AudioVoice voice)
    {
        VerifyVoice(voice);
        voice.Playing = true;
    }

    [ExecutionCommand(Sender = "Voice")]
    private void Stop([ExecutionTarget] AudioVoice voice)
    {
        VerifyVoice(voice);
        voice.Playing = false;
    }

    [ExecutionCommand(Sender = "Voice")]
    private void Destroy([ExecutionTarget] AudioVoice voice)
    {
        VerifyVoice(voice);
        voice.Playing = false;
        voice.Destroyed = true;
        _voices.Remove(voice);
    }

    [ExecutionCommand]
    private void Close()
    {
        VerifyOwner();
        foreach (AudioVoice voice in _voices)
        {
            voice.Destroyed = true;
            voice.Playing = false;
        }

        _voices.Clear();
        _initialized = false;
    }

    [ExecutionUpdate]
    private void UpdateControl(in UpdateTick tick)
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

    private void VerifyVoice(AudioVoice voice)
    {
        VerifyOwner();
        if (voice.Owner != this || voice.Destroyed || !_voices.Contains(voice))
        {
            throw new InvalidOperationException("Voice belongs to another device or has been destroyed.");
        }
    }
}
