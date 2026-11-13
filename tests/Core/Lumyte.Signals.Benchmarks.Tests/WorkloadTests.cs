using Lumyte.Signals.Benchmark;
using Xunit;

namespace Lumyte.Signals.Benchmarks.Tests;

/// <summary>Checks benchmark workloads independently of timing and allocation measurements.</summary>
public sealed class WorkloadTests
{
    /// <summary>Checks primitive workloads can read and write their prepared signals.</summary>
    [Fact]
    public void ScalarReadsAndWrites()
    {
        var benchmark = new ScalarBenchmarks();
        Assert.Equal(42, benchmark.ReadValue());
        Assert.Equal(42, benchmark.ReadPeek());
        benchmark.WriteSame();
        benchmark.WriteUnobserved();
    }

    /// <summary>Checks updates reach the final observer at each fanout size.</summary>
    /// <param name="observers">The number of observers.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(128)]
    public void FanoutDeliversUpdates(int observers)
    {
        var benchmark = new FanoutBenchmarks { Observers = observers };
        benchmark.Setup();
        try
        {
            Assert.Equal(1, benchmark.Update());
            Assert.Equal(2, benchmark.Update());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks cached and updated chains with and without observers.</summary>
    /// <param name="depth">The number of computed nodes.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void ComputedChainsStayFresh(int depth)
    {
        var benchmark = new ComputedBenchmarks { Depth = depth };
        benchmark.Setup();
        try
        {
            Assert.Equal(depth, benchmark.ReadObservedCached());
            Assert.Equal(depth, benchmark.ReadUnobservedCached());
            Assert.Equal(depth + 1, benchmark.UpdateObserved());
            Assert.Equal(depth + 2, benchmark.UpdateAndReadUnobserved());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks diamond, branch switching, and captured read workloads.</summary>
    [Fact]
    public void GraphUpdatesAndReadSets()
    {
        var benchmark = new GraphBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.Equal(4, benchmark.DiamondUpdate());
            Assert.Equal(1, benchmark.SwitchBranch());
            Assert.True(benchmark.CheckReadSet());
            Assert.True(benchmark.CaptureEightReads().IsCurrent);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks both batch workloads deliver their final value.</summary>
    [Fact]
    public void BatchesDeliverFinalValues()
    {
        var benchmark = new BatchBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.Equal(16, benchmark.Unbatched16());
            Assert.Equal(32, benchmark.Batched16());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks the manually scheduled workload drains its pending delivery.</summary>
    [Fact]
    public void SchedulerDeliversFinalValue()
    {
        var benchmark = new SchedulerBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.Equal(16, benchmark.Coalesced16());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks completed reloads notify once for each transition and stale completions stay canceled.</summary>
    [Fact]
    public void AsyncTransitionsAndCancellation()
    {
        var benchmark = new AsyncBenchmarks();
        benchmark.Setup();
        try
        {
            Assert.Equal(42, benchmark.CreateCompleted());
            Assert.Equal(42, benchmark.ReloadCompleted());
            int notifications = benchmark.Notifications;
            Assert.Equal(42, benchmark.ReloadCompletedObserved());
            Assert.Equal(2, benchmark.Notifications - notifications);
            Assert.Equal(2, benchmark.SupersedeAndCancelPending());
            Assert.Equal(AsyncSignalStatus.Canceled, benchmark.PendingStatus);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks temporary observers are detached while the stable observer keeps its dependencies.</summary>
    /// <param name="dependencies">The number of inputs read by an observer.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(128)]
    public void ReusePreservesObserverLifetime(int dependencies)
    {
        var benchmark = new ReuseBenchmarks { Dependencies = dependencies };
        benchmark.Setup();
        try
        {
            int initial = dependencies * (dependencies - 1) / 2;
            Assert.Equal(initial, benchmark.Sum);
            Assert.Equal(1, benchmark.Notifications);
            Assert.Equal(initial + 1, benchmark.ReevaluateStable());
            Assert.Equal(2, benchmark.Notifications);
            Assert.Equal(initial + 1, benchmark.CreateAndDisposeEffect());
            Assert.Equal(3, benchmark.Notifications);
            Assert.Equal(initial + 2, benchmark.ReevaluateStable());
            Assert.Equal(4, benchmark.Notifications);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks related and unrelated mutations preserve the latest shared graph value.</summary>
    /// <param name="layers">The number of shared two-node layers.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void ValidationPreservesFreshness(int layers)
    {
        var benchmark = new ValidationBenchmarks { Layers = layers };
        benchmark.Setup();
        int factor = 1 << (layers - 1);
        Assert.Equal(factor, benchmark.ReadAfterUnrelatedWrite());
        Assert.Equal(factor * 2, benchmark.UpdateAndRead());
        Assert.Equal(factor * 2, benchmark.ReadAfterUnrelatedWrite());
        Assert.Equal(factor * 3, benchmark.UpdateAndRead());
    }

    /// <summary>Checks repeated cached reads of observed and passive shared graphs.</summary>
    /// <param name="layers">The number of shared two-node layers.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void SharedDagPreservesCachedValues(int layers)
    {
        var benchmark = new SharedDagBenchmarks { Layers = layers };
        benchmark.Setup();
        try
        {
            int expected = 1 << layers;
            Assert.Equal(expected, benchmark.ReadObservedCached());
            Assert.Equal(expected, benchmark.ReadUnobservedCached());
            Assert.Equal(expected, benchmark.ReadObservedCached());
            Assert.Equal(expected, benchmark.ReadUnobservedCached());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
