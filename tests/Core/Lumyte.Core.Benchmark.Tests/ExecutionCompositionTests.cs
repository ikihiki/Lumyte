using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Checks independent execution and composition benchmark consumers.</summary>
public sealed class ExecutionCompositionTests
{
    /// <summary>Runs deterministic runtime and compile-time oracles.</summary>
    [Fact]
    public void VerifyConsumers()
    {
        Execution.Benchmark.BenchmarkChecks.Verify();
        Composition.Benchmark.BenchmarkChecks.Verify();
        Execution.Generators.Benchmark.BenchmarkChecks.Verify();
        Composition.Generators.Benchmark.BenchmarkChecks.Verify();
    }

    /// <summary>Checks target declaration mutations change generated output and both consumers compile.</summary>
    [Fact]
    public void TargetMutationsInvalidateGeneratedOutput()
    {
        var execution = new Execution.Generators.Benchmark.GeneratorBenchmarks { Count = 1, Change = "Target" };
        execution.Setup();
        Assert.True(execution.TargetOutputChanges());
        Assert.Equal(0, execution.ConsumerCompile());
        Assert.Equal(0, execution.ChangedConsumerCompile());
        var composition = new Composition.Generators.Benchmark.GeneratorBenchmarks { Count = 1, Change = "Target" };
        composition.Setup();
        Assert.True(composition.TargetOutputChanges());
        Assert.Equal(0, composition.ConsumerCompile());
        Assert.Equal(0, composition.ChangedConsumerCompile());
    }

    /// <summary>Checks retained capacity and cancellation status independently.</summary>
    [Fact]
    public void AdmissionBoundaries()
    {
        var benchmark = new Execution.Benchmark.LifecycleBenchmarks();
        Assert.Equal(Execution.CommandEnqueueStatus.QueueFull, benchmark.RetainedCapacity());
        Assert.Equal(Execution.CommandEnqueueStatus.Canceled, benchmark.PreCanceled());
        Assert.Equal(14, benchmark.GateRelease());
    }

    /// <summary>Checks the migrated direct and delegate controls and whole sixteen-command batch.</summary>
    [Fact]
    public void LegacySenderControlsAndBatchSixteen()
    {
        var controls = new Execution.Benchmark.SenderControlBenchmarks();
        controls.Setup();
        Assert.Equal(14, controls.Direct());
        Assert.Equal(14, controls.CachedDelegate());
        Assert.Equal(14, controls.CapturedDelegate());
        var benchmark = new Execution.Benchmark.ExecutionBenchmarks { Count = 16 };
        benchmark.Setup();
        try
        {
            Assert.Equal(30, benchmark.Batch());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks omitted and explicit zero factory arguments and independent arrays.</summary>
    [Fact]
    public void GeneratedCompositionBoundaries()
    {
        Assert.Equal(7, Composition.Benchmark.Compose.Item().Value);
        Assert.Equal(0, Composition.Benchmark.Compose.Item(value: 0).Value);
        var benchmark = new Composition.Benchmark.CompositionBenchmarks { Count = 1 };
        benchmark.Setup();
        int[] first = benchmark.Apply();
        first[0] = -1;
        Assert.Equal(new[] { 1 }, benchmark.Apply());
        Assert.Equal(2, benchmark.Adapter());
    }
}
