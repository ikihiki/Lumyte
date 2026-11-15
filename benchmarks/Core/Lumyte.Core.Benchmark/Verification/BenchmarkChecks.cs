using Lumyte.Core.Benchmark.ClockTime;
using Lumyte.Core.Time;

namespace Lumyte.Core.Benchmark;

/// <summary>Deterministic time contracts independent of measured duration.</summary>
public static class BenchmarkChecks
{
    /// <summary>Checks arithmetic, overflow, rejection and monotonicity.</summary>
    public static void Verify()
    {
        var benchmark = new TimeBenchmarks();
        if (benchmark.Add().Ticks != 11913 || benchmark.Subtract().Ticks != 12777 || benchmark.Advance() != 4096)
        {
            throw new InvalidOperationException("Time arithmetic mismatch.");
        }

        var clock = new SystemMonotonicClock();
        long previous = 0;
        for (int i = 0; i < 4096; i++)
        {
            long current = clock.Now.Ticks;
            if (current < previous)
            {
                throw new InvalidOperationException("Clock moved backwards.");
            }

            previous = current;
        }

        var manual = new ManualClock(TimePoint.FromTicks(long.MaxValue));
        Expect<OverflowException>(() => manual.Advance(Duration.FromTicks(1)));
        Expect<ArgumentOutOfRangeException>(() => manual.Advance(Duration.FromTicks(-1)));
        Expect<ArgumentOutOfRangeException>(() => Duration.FromSeconds(double.NaN));
        Expect<OverflowException>(() => _ = -Duration.FromTicks(long.MinValue));
        if (manual.Now.Ticks != long.MaxValue)
        {
            throw new InvalidOperationException("Rejected advance mutated clock.");
        }
    }

    private static void Expect<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
