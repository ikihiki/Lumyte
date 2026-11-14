using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace Lumyte.Execution.Benchmark;

/// <summary>Controls for the generated sender using its exact receiver operation.</summary>
[MemoryDiagnoser]
public class SenderControlBenchmarks
{
    private readonly SenderState _state = new();
    private Func<int> _cached = null!;
    private Func<int>? _escaped;

    internal int ObservedValue => _state.Value;

    /// <summary>Creates the retained delegate outside measurement.</summary>
    [GlobalSetup]
    public void Setup() => _cached = () => _state.Play(7);

    /// <summary>Invokes the identical receiver work without execution admission.</summary>
    /// <returns>The command result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-EXEC-02")]
    public int Direct() => _state.Play(7);

    /// <summary>Invokes the retained receiver delegate.</summary>
    /// <returns>The command result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-EXEC-02")]
    public int CachedDelegate() => _cached();

    /// <summary>Creates an escaping receiver closure and then invokes it once.</summary>
    /// <returns>The command result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-EXEC-02")]
    public int CapturedDelegate() => CreateCaptured(_state, 7)();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Func<int> CreateCaptured(SenderState state, int value)
    {
        _escaped = () => state.Play(value);
        return _escaped;
    }
}
