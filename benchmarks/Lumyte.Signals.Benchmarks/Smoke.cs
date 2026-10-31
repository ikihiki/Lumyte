namespace Lumyte.Signals.Benchmarks;

internal static class Smoke
{
    internal static void Verify()
    {
        var scalar = new ScalarBenchmarks();
        Require(scalar.ReadValue() == 42 && scalar.ReadPeek() == 42, "Primitive read result");
        scalar.WriteSame();
        scalar.WriteUnobserved();

        foreach (int observers in new[] { 1, 16, 128 })
        {
            var fanout = new FanoutBenchmarks { Observers = observers };
            fanout.Setup();
            Require(fanout.Update() == 1 && fanout.Update() == 2, "Fanout final result");
            fanout.Cleanup();
        }

        foreach (int depth in new[] { 1, 8, 32 })
        {
            var computed = new ComputedBenchmarks { Depth = depth };
            computed.Setup();
            Require(computed.ReadObservedCached() == depth && computed.ReadUnobservedCached() == depth, "Cached chain result");
            Require(computed.UpdateObserved() == depth + 1 && computed.UpdateAndReadUnobserved() == depth + 2, "Updated chain result");
            computed.Cleanup();
        }

        var graph = new GraphBenchmarks();
        graph.Setup();
        Require(graph.DiamondUpdate() == 4 && graph.SwitchBranch() == 1, "Diamond and branch result");
        Require(graph.CheckReadSet() && graph.CaptureEightReads().IsCurrent, "Passive read set");
        graph.Cleanup();

        var batch = new BatchBenchmarks();
        batch.Setup();
        Require(batch.Unbatched16() == 16 && batch.Batched16() == 32, "Batch final result");
        batch.Cleanup();

        var scheduler = new SchedulerBenchmarks();
        scheduler.Setup();
        Require(scheduler.Coalesced16() == 16, "Scheduled final result");
        scheduler.Cleanup();

        var source = Signal.Create(0);
        int calls = 0;
        using SignalEffect effect = Signal.Effect(() =>
        {
            _ = source.Value;
            calls++;
        });
        for (int index = 1; index <= 16; index++)
        {
            source.Value = index;
        }

        Require(calls == 17, "Sixteen unbatched notifications");
        Signal.Batch(() =>
        {
            for (int index = 17; index <= 32; index++)
            {
                source.Value = index;
            }
        });
        Require(calls == 18, "One batched notification");
        AsyncBenchmarks.Verify();
        ReuseBenchmarks.Verify();
        ValidationBenchmarks.Verify();
        SharedDagBenchmarks.Verify();
    }

    private static void Require(bool condition, string operation)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Benchmark workload check failed: {operation}.");
        }
    }
}
