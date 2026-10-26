namespace Lumyte.Execution.Sample;

/// <summary>A one-reference sender wrapping a fully created backend voice.</summary>
/// <param name="original">The fully created original instance.</param>
public readonly partial struct DemoVoice(DemoVoiceData original)
{
    private readonly DemoVoiceData _original = original;

    [ExecutionCommand]
    private void PlayCore() => _original.Owner.Play(_original);

    [ExecutionCommand]
    private void StopCore() => _original.Owner.Stop(_original);

    [ExecutionCommand]
    private void DestroyCore() => _original.Owner.Destroy(_original);
}
