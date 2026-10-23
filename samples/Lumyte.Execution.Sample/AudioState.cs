namespace Lumyte.Execution.Sample;

/// <summary>A one-reference sender for the sample backend.</summary>
/// <param name="original">The fully created original instance.</param>
public readonly partial struct AudioState(AudioBackend original)
{
    private readonly AudioBackend _original = original;

    /// <summary>Gets the backend creation thread.</summary>
    public int ThreadId => _original.ThreadId;

    /// <summary>Gets the backend update count.</summary>
    public int UpdateCount => _original.UpdateCount;

    internal void PlayDirect(AudioVoiceData voice) => _original.Play(voice);

    [ExecutionCommand]
    private void InitializeCore() => _original.Initialize();

    [ExecutionCommand]
    private AudioVoiceData CreateVoiceCore(int clip) => _original.CreateVoice(clip);

    [ExecutionCommand]
    private void CloseCore() => _original.Close();

    [ExecutionUpdate]
    private void UpdateControl(in UpdateTick tick) => _original.UpdateControl(in tick);
}
