using Lumyte.Memory.Benchmark.Management;
using Lumyte.Resources.Benchmark.Streaming;
using Lumyte.Signals.Benchmark.Async;
using Lumyte.Signals.Benchmark.Scheduling;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Deterministic contracts for independent runtime workloads and category boundaries.</summary>
public class RuntimeWorkloadTests
{
    /// <summary>Every new stream cursor must restart, consume all elements and close before cleanup.</summary>
    /// <param name="length">The finite stream length.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    [InlineData(65536)]
    public async Task IndependentStreamsRestartAndCompleteAsync(int length)
    {
        var benchmark = new ResourceStreamBenchmarks { Length = length };
        benchmark.Setup();
        try
        {
            for (int repeat = 0; repeat < 2; repeat++)
            {
                Assert.Equal((long)length * (length - 1) / 2, await benchmark.ReadAllAsync());
                Assert.Equal(length == 0 ? -1 : length / 2, await benchmark.SeekAndReadAsync());
                Assert.Equal((repeat + 1) * 2, benchmark.ReadersOpened);
                Assert.Equal(benchmark.ReadersOpened, benchmark.ReadersClosed);
            }
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Budget refusal is real, recovery works, and every invocation restores the ledger.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task BudgetRefusalAndEstimatedAccountingResetAsync()
    {
        var benchmark = new BudgetBenchmarks();
        benchmark.Setup();
        try
        {
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Assert.Equal(1, await benchmark.RejectAndRecoverAsync());
                Assert.Equal(1024UL, await benchmark.EstimatedChargeAsync());
                Assert.Equal(0UL, benchmark.Inspect().ReservedBytes);
                Assert.Equal(0UL, benchmark.Inspect().CommittedBytes);
                Assert.Equal(0UL, benchmark.Inspect().EstimatedBytes);
                Assert.Equal(1024UL * (ulong)(repeat + 1), benchmark.Inspect().ReleasedBytes);
            }
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Scheduled delivery coalesces and paused work is revoked without losing resume delivery.</summary>
    [Fact]
    public void ScheduledEffectsAreNonvacuousAndReusable()
    {
        var benchmark = new SchedulerBenchmarks();
        for (int reset = 0; reset < 2; reset++)
        {
            benchmark.Setup();
            try
            {
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    Assert.Equal((repeat * 24) + 16, benchmark.Coalesced16());
                    Assert.Equal((repeat + 1) * 24, benchmark.PauseResume());
                }
            }
            finally
            {
                benchmark.Cleanup();
            }
        }
    }

    /// <summary>Pending operations complete before return and failed loaders recover on the same adapter.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task AsyncTerminalPathsCompleteAsync()
    {
        var benchmark = new AsyncCompletionBenchmarks();
        for (int repeat = 0; repeat < 3; repeat++)
        {
            Assert.Equal(42, await benchmark.PendingCompletionAsync());
            Assert.Equal(42, await benchmark.FailureAndRecoveryAsync());
        }
    }

    /// <summary>Post-admission cancellation retires its completion and releases the only command slot.</summary>
    [Fact]
    public void AdmittedCancellationReleasesCapacity()
    {
        var benchmark = new Execution.Benchmark.Runtime.LifecycleBenchmarks();
        Assert.Equal(1, benchmark.CancelAdmitted());
        Assert.Equal(1, benchmark.CancelAdmitted());
    }
}
