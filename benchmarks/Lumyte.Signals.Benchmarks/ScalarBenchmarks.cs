using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmarks;

/// <summary>Measures primitive operations with values and callbacks created outside timed code.</summary>
public class ScalarBenchmarks
{
    private readonly Signal<int> _read = Signal.Create(42);
    private readonly Signal<int> _same = Signal.Create(42);
    private readonly Signal<int> _changed = Signal.Create(0);
    private int _next;

    /// <summary>Reads a mutable value outside dependency tracking.</summary>
    /// <returns>The current value.</returns>
    [Benchmark]
    public int ReadValue() => _read.Value;

    /// <summary>Reads without dependency tracking.</summary>
    /// <returns>The current value.</returns>
    [Benchmark]
    public int ReadPeek() => _read.Peek();

    /// <summary>Assigns an equal value with no observers.</summary>
    [Benchmark]
    public void WriteSame() => _same.Value = 42;

    /// <summary>Assigns a different value with no observers.</summary>
    [Benchmark]
    public void WriteUnobserved() => _changed.Value = ++_next;
}
