using System.Reflection;
using BenchmarkDotNet.Attributes;
using Lumyte.Memory.Benchmark;
using Lumyte.Resources.Benchmark;
using Lumyte.Signals.Benchmark;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Deterministic contracts for independent runtime workloads and category boundaries.</summary>
public class StandaloneRuntimeTests
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
        var benchmark = new StandaloneStreamingBenchmarks { Length = length };
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
        var benchmark = new Execution.Benchmark.LifecycleBenchmarks();
        Assert.Equal(1, benchmark.CancelAdmitted());
        Assert.Equal(1, benchmark.CancelAdmitted());
    }

    /// <summary>Standalone labels never leak onto DI, embedded, Execution-adapter or Composition integration cases.</summary>
    [Fact]
    public void RuntimeCategoriesSeparateExcludedIntegrations()
    {
        Assembly[] assemblies =
        [
            typeof(Execution.Benchmark.ExecutionBenchmarks).Assembly,
            typeof(Worlds.Benchmark.WorldsBenchmarks).Assembly,
            typeof(ScalarBenchmarks).Assembly,
            typeof(StateMachines.Benchmark.StateMachinesBenchmarks).Assembly,
            typeof(MemoryBenchmarks).Assembly,
            typeof(ResourcesBenchmarks).Assembly,
        ];
        int standalone = 0;
        int excluded = 0;
        foreach (Type type in assemblies.SelectMany(assembly => assembly.GetTypes()))
        {
            foreach (MethodInfo method in type.GetMethods().Where(method => method.IsDefined(typeof(BenchmarkAttribute))))
            {
                string[] categories = type.GetCustomAttributes<BenchmarkCategoryAttribute>().Concat(method.GetCustomAttributes<BenchmarkCategoryAttribute>()).SelectMany(attribute => attribute.Categories).ToArray();
                bool isExcluded = categories.Contains("Integration") || categories.Contains("DependencyInjection");
                Assert.Equal(!isExcluded, categories.Contains("Standalone"));
                standalone += isExcluded ? 0 : 1;
                excluded += isExcluded ? 1 : 0;
            }
        }

        Assert.True(standalone > 80);
        Assert.True(excluded >= 10);
    }
}
