using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class RetentionProbe
{
    internal static void Run()
    {
        for (int repeat = 0; repeat < 3; repeat++)
        {
            foreach (string pattern in new[] { "static", "single", "scattered", "all" })
            {
                foreach (int chunk in new[] { 1, 64 })
                {
                    foreach (string mode in new[] { "legacy", "retained", "flatten" })
                    {
                        if (mode == "legacy" && chunk != 1)
                        {
                            continue;
                        }

                        Console.WriteLine(JsonSerializer.Serialize(Measure(repeat, pattern, chunk, mode)));
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Measure(int repeat, string pattern, int chunk, string mode)
    {
        var warm = new ComparisonCase(128, pattern, "rectangles", chunk, mode != "legacy", mode == "flatten");
        for (int i = 0; i < 20; i++)
        {
            Advance(warm, mode);
        }

        return MeasureReady(repeat, pattern, chunk, mode);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object MeasureReady(int repeat, string pattern, int chunk, string mode)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long before = GC.GetTotalMemory(true);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var state = new ComparisonCase(10000, pattern, "rectangles", chunk, mode != "legacy", mode == "flatten");
        long initialAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        for (int i = 0; i < 100; i++)
        {
            Advance(state, mode);
        }

        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(state);
        return new { baseline = Environment.GetEnvironmentVariable("DRAWING_BASELINE"), repeat, count = 10000, pattern, chunk, mode, initialAllocated, retainedHeapBytes = after - before, cpuSnapshots = 3, frames = 100, scope = "managed live heap delta, not RSS/VRAM" };
    }

    private static void Advance(ComparisonCase state, string mode)
    {
        if (mode == "legacy")
        {
            state.LegacyFrame();
        }
        else if (mode == "retained")
        {
            state.RetainedFrame();
        }
        else
        {
            state.RetainedAndFlattenFrame();
        }
    }
}
