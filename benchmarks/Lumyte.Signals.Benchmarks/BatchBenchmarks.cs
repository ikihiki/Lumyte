using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmarks;

/// <summary>Compares complete transactions of sixteen writes, including final delivery.</summary>
public class BatchBenchmarks
{
    private const int Writes = 16;
    private Signal<int> _source = null!;
    private SignalEffect _effect = null!;
    private Action _update = null!;
    private int _next;
    private int _sink;

    /// <summary>Creates one subscription and caches the batch callback.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _source = Signal.Create(0);
        _effect = Signal.Effect(() => _sink = _source.Value);
        _update = WriteAll;
    }

    /// <summary>Performs sixteen writes and sixteen effect deliveries.</summary>
    /// <returns>The final observed value.</returns>
    [Benchmark]
    public int Unbatched16()
    {
        WriteAll();
        return _sink;
    }

    /// <summary>Performs sixteen writes and one effect delivery.</summary>
    /// <returns>The final observed value.</returns>
    [Benchmark]
    public int Batched16()
    {
        Signal.Batch(_update);
        return _sink;
    }

    /// <summary>Releases the observer.</summary>
    [GlobalCleanup]
    public void Cleanup() => _effect.Dispose();

    private void WriteAll()
    {
        for (int index = 0; index < Writes; index++)
        {
            _source.Value = ++_next;
        }
    }
}
