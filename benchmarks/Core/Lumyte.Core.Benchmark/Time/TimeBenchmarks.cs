using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;

namespace Lumyte.Core.Benchmark;

/// <summary>Monotonic reads and checked time arithmetic.</summary>
[BenchmarkCategory("Standalone", "Core", "Time", "Screening", "Comparison", "Diagnostic")]
public class TimeBenchmarks
{
    private readonly SystemMonotonicClock _clock = new();
    private Duration _left = Duration.FromTicks(12345);
    private Duration _right = Duration.FromTicks(-432);
    private TimePoint _point = TimePoint.FromTicks(100000);

    /// <summary>Constructs a new clock origin.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Construction")]
    public SystemMonotonicClock Construct() => new();

    /// <summary>Reads elapsed ticks from an existing origin.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public long Read() => _clock.Now.Ticks;

    /// <summary>Adds signed durations.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public Duration Add() => _left + _right;

    /// <summary>Subtracts signed durations.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public Duration Subtract() => _left - _right;

    /// <summary>Negates a duration.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public Duration Negate() => -_left;

    /// <summary>Scales a duration.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public Duration Multiply() => _left * 1.25;

    /// <summary>Divides a duration.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public Duration Divide() => _left / 1.25;

    /// <summary>Converts fractional seconds.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public Duration Seconds() => Duration.FromSeconds(_left.TotalSeconds);

    /// <summary>Computes a point difference.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public Duration Difference() => _point - TimePoint.FromTicks(_right.Ticks);

    /// <summary>Advances a time point.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public TimePoint PointAdd() => _point + _left;

    /// <summary>Moves a time point backwards.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public TimePoint PointSubtract() => _point - _left;

    /// <summary>Advances a bounded deterministic clock wave.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public long Advance()
    {
        var clock = new ManualClock();
        for (int i = 0; i < 4096; i++)
        {
            clock.Advance(Duration.FromTicks(1));
        }

        return clock.Now.Ticks;
    }
}
