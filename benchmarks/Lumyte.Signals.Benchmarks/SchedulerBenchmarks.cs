using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmarks;

/// <summary>Measures scheduler reservation coalescing without operating system dispatch latency.</summary>
public class SchedulerBenchmarks
{
    private readonly ManualScheduler _scheduler = new();
    private Signal<int> _source = null!;
    private SignalEffect _effect = null!;
    private int _next;
    private int _sink;

    /// <summary>Creates an observer with a manual same-thread scheduler.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _source = Signal.Create(0);
        _effect = Signal.Effect(() => _sink = _source.Value, _scheduler);
    }

    /// <summary>Changes sixteen times, then drains the single scheduled delivery.</summary>
    /// <returns>The final observed value.</returns>
    [Benchmark]
    public int Coalesced16()
    {
        for (int index = 0; index < 16; index++)
        {
            _source.Value = ++_next;
        }

        _scheduler.Drain();
        return _sink;
    }

    /// <summary>Releases the observer.</summary>
    [GlobalCleanup]
    public void Cleanup() => _effect.Dispose();

    private sealed class ManualScheduler : ISignalScheduler
    {
        private readonly Queue<Action> _work = new(1);

        public void Schedule(Action work) => _work.Enqueue(work);

        internal void Drain()
        {
            while (_work.TryDequeue(out Action? work))
            {
                work();
            }
        }
    }
}
