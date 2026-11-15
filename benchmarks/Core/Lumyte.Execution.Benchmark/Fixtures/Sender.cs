namespace Lumyte.Execution.Benchmark;

/// <summary>Independent generated receiver preserving owner access and observable mutation.</summary>
/// <param name="original">The receiver-owned result storage.</param>
public readonly partial struct Sender(SenderState original)
{
    private readonly SenderState _original = original;

    [ExecutionCommand]
    private int PlayCore(int value) => _original.Play(value);
}
