using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>Dedicated post-full-GC managed live-heap and process RSS probe; never invoke inside frame timing.</summary>
public static class ManagedRetentionProbe
{
    /// <summary>Preserves the original baseline/initialized/cycle managed-heap array shape.</summary>
    /// <param name="retained">Use public Scene or immutable Canvas.</param>
    /// <param name="granularity">Commands per Scene content node.</param>
    /// <param name="snapshotHistory">Zero, one or three past changed generations.</param>
    /// <param name="cycles">Fixed number of roundtrips, from one through 64.</param>
    /// <returns>Legacy managed heap observations without the additional after-dispose checkpoint.</returns>
    public static long[] Measure(bool retained, int granularity, int snapshotHistory, int cycles = 8) => MeasureDetailed(retained, granularity, snapshotHistory, cycles).Take(cycles + 2).Select(sample => sample.ManagedLiveHeapBytes).ToArray();

    /// <summary>Measures baseline, initialization, fixed return cycles and post-release live memory separately.</summary>
    /// <param name="retained">Use public Scene or immutable Canvas.</param>
    /// <param name="granularity">Commands per Scene content node.</param>
    /// <param name="snapshotHistory">Zero, one or three past changed generations.</param>
    /// <param name="cycles">Fixed number of roundtrips, from one through 64; never adaptive stopping.</param>
    /// <returns>Ordered observations containing only scalar data, never the measured fixture owner.</returns>
    public static RetentionMemorySample[] MeasureDetailed(bool retained, int granularity, int snapshotHistory, int cycles = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cycles, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cycles, 64);
        RectangleTrace.ValidateGranularity(granularity);
        if (snapshotHistory is not (0 or 1 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotHistory));
        }

        var samples = new RetentionMemorySample[cycles + 3];
        using var process = Process.GetCurrentProcess();
        samples[0] = Observe(process, "baseline", null, null, 0, false);
        PopulateOwned(process, samples, retained, granularity, snapshotHistory, cycles);

        // NoInlining ensures no fixture local survives on this stack during the release observation.
        samples[^1] = Observe(process, "after-dispose", null, null, 0, false);
        return samples;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PopulateOwned(Process process, RetentionMemorySample[] samples, bool retained, int granularity, int snapshotHistory, int cycles)
    {
        using var fixture = new RectangleFixture(new("burst", "contiguous"), granularity, retained, snapshotHistory: snapshotHistory);
        samples[1] = Observe(process, "initialized", null, 0, fixture.History.Items.Count, true);
        GC.KeepAlive(fixture);
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            fixture.Apply(RectangleTrace.FirstBurstFrame + (2 * cycle * RectangleTrace.BurstPeriod));
            int frame = RectangleTrace.FirstBurstFrame + (((2 * cycle) + 1) * RectangleTrace.BurstPeriod);
            fixture.Apply(frame);
            samples[cycle + 2] = Observe(process, "cycle-return", cycle + 1, frame, fixture.History.Items.Count, true);
            GC.KeepAlive(fixture);
        }

        GC.KeepAlive(fixture);
    }

    private static RetentionMemorySample Observe(Process process, string stage, int? cycle, int? logicalFrame, int retainedGenerations, bool fixtureOwned)
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        long managed = GC.GetTotalMemory(forceFullCollection: false);
        process.Refresh();
        long rss = process.WorkingSet64;
        return new(stage, cycle, logicalFrame, retainedGenerations, fixtureOwned, managed, rss);
    }
}
