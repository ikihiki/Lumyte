using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmarks;

/// <summary>Measures the cost of one mutation as the number of observers increases.</summary>
public class FanoutBenchmarks
{
    private Signal<int> _source = null!;
    private SignalEffect[] _effects = [];
    private int _next;
    private int _sink;

    /// <summary>Gets or sets the active observer count.</summary>
    [Params(1, 16, 128)]
    public int Observers { get; set; }

    /// <summary>Creates all subscriptions outside measured iterations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _source = Signal.Create(0);
        _effects = new SignalEffect[Observers];
        for (int index = 0; index < Observers; index++)
        {
            _effects[index] = Signal.Effect(() => _sink = _source.Value);
        }
    }

    /// <summary>Updates once and delivers all observers.</summary>
    /// <returns>The last observed value.</returns>
    [Benchmark]
    public int Update()
    {
        _source.Value = ++_next;
        return _sink;
    }

    /// <summary>Detaches all observers.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (SignalEffect effect in _effects)
        {
            effect.Dispose();
        }
    }
}
