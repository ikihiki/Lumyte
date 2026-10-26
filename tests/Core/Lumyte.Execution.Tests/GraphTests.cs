using System.Diagnostics;
using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks dependency ordering, bounded propagation, range parallelism, and owner delivery.</summary>
public sealed class GraphTests
{
    /// <summary>Cooperative graph chunks and owner work advance within a caller-supplied budget.</summary>
    [Fact]
    public void CooperativeGraphProducesRangeThenAppliesOnHost()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        module.Gate!.Open();
        JobGraph<GraphInput> graph = Build(module);
        var data = new GraphData(31);
        var input = new GraphInput(data);
        Assert.True(runtime.TrySchedule(graph, in input, out JobRun? run));
        Assert.Equal(1, runtime.PumpJobs(1, TimeSpan.FromSeconds(1)));
        Assert.False(run!.IsCompleted);
        PumpUntil(runtime, module, () => run.IsCompleted);
        run.ThrowIfFailed();
        Assert.Equal(496, data.Total);
        Assert.Equal(Environment.CurrentManagedThreadId, data.ApplyThread);
        Assert.Equal("prepare", data.Events.First());
        Assert.Equal("apply", data.Events.Last());
        run.Dispose();
        Stop(runtime, module);
    }

    /// <summary>Range workers claim disjoint chunks and stay within CPU concurrency.</summary>
    [Fact]
    public void ThreadPoolRangesRunInParallelWithinLimit()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        module.Gate!.Open();
        var data = new GraphData(512) { SlowRange = true };
        var input = new GraphInput(data);
        Assert.True(runtime.TrySchedule(Build(module), in input, out JobRun? run));
        PumpUntil(runtime, module, () => run!.IsCompleted);
        run!.ThrowIfFailed();
        Assert.Equal(Enumerable.Range(1, 512), data.Values);
        Assert.InRange(module.Peak, 1, 2);
        Assert.NotEqual(Environment.CurrentManagedThreadId, data.ApplyThread);
        run.Dispose();
        Stop(runtime, module);
    }

    /// <summary>A waiting owner does not occupy the sole CPU slot.</summary>
    [Fact]
    public void OwnerGateWaitDoesNotBlockIndependentCpuCommand()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        var input = new GraphInput(new GraphData(10));
        Assert.True(runtime.TrySchedule(Build(module), in input, out JobRun? run));
        ValueTask<Unit> independent = runtime.Bind(module.Prepare).ExecuteAsync(input);
        PumpUntil(runtime, module, () => independent.IsCompleted);
        CompletedResult.Read(independent);
        Assert.False(run!.IsCompleted);
        module.Gate!.Open();
        PumpUntil(runtime, module, () => run.IsCompleted);
        run.ThrowIfFailed();
        run.Dispose();
        Stop(runtime, module);
    }

    /// <summary>A predecessor failure suppresses dependent chunks and owner side effects.</summary>
    [Fact]
    public void FailureSuppressesDependentNodes()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        module.Gate!.Open();
        var data = new GraphData(20) { FailPrepare = true };
        var input = new GraphInput(data);
        Assert.True(runtime.TrySchedule(Build(module), in input, out JobRun? run));
        PumpUntil(runtime, module, () => run!.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => run!.ThrowIfFailed());
        Assert.Empty(data.Events);
        run!.Dispose();
        Stop(runtime, module);
    }

    /// <summary>Cancellation handles dormant nodes and completes after bounded propagation.</summary>
    [Fact]
    public void CancelBeforeAnyNodeSuppressesAllSideEffects()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        var data = new GraphData(20);
        var input = new GraphInput(data);
        Assert.True(runtime.TrySchedule(Build(module), in input, out JobRun? run));
        run!.RequestCancel();
        PumpUntil(runtime, module, () => run.IsCompleted);
        Assert.Throws<OperationCanceledException>(() => run.ThrowIfFailed());
        Assert.Empty(data.Events);
        run.Dispose();
        Stop(runtime, module);
    }

    /// <summary>Empty ranges succeed without callbacks and reusable graphs accept a new context.</summary>
    [Fact]
    public void ZeroLengthRangesAndGraphReuseAreSupported()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        module.Gate!.Open();
        JobGraph<GraphInput> graph = Build(module);
        foreach (int length in new[] { 0, 3 })
        {
            var data = new GraphData(length);
            var input = new GraphInput(data);
            Assert.True(runtime.TrySchedule(graph, in input, out JobRun? run));
            PumpUntil(runtime, module, () => run!.IsCompleted);
            run!.ThrowIfFailed();
            Assert.Equal(length * (length + 1) / 2, data.Total);
            if (length == 0)
            {
                Assert.DoesNotContain("range", data.Events);
            }

            run.Dispose();
        }

        Stop(runtime, module);
    }

    /// <summary>Adjacent ranges with different worker counts retain independent claims across graph reuse.</summary>
    /// <param name="mode">The CPU scheduling mode.</param>
    [Theory]
    [InlineData(CpuExecutionMode.Cooperative)]
    [InlineData(CpuExecutionMode.ThreadPool)]
    public void MultipleRangesWithDifferentChunkSizesCompleteExactlyOnce(CpuExecutionMode mode)
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, mode, mode == CpuExecutionMode.Cooperative ? 1 : 3, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        module.Gate!.Open();
        var builder = new JobGraphBuilder<GraphInput>();
        JobNodeId previous = builder.Add("Prepare", module.Prepare);
        foreach (int chunkSize in new[] { 1, 8, 64 })
        {
            JobNodeId range = builder.AddRange($"Range{chunkSize}", module.Range, chunkSize);
            builder.DependsOn(range, previous);
            previous = range;
        }

        JobNodeId apply = builder.Add("Apply", module.Apply);
        builder.DependsOn(apply, previous);
        JobGraph<GraphInput> graph = builder.Build();
        foreach (int length in new[] { 0, 1, 17 })
        {
            var data = new GraphData(length);
            var input = new GraphInput(data);
            Assert.True(runtime.TrySchedule(graph, in input, out JobRun? run));
            PumpUntil(runtime, module, () => run!.IsCompleted);
            run!.ThrowIfFailed();
            Assert.Equal(length * (length + 1) / 2, data.Total);
            Assert.Equal(length + ((length + 7) / 8) + ((length + 63) / 64), data.Events.Count(value => value == "range"));
            Assert.Equal("prepare", data.Events.First());
            Assert.Equal("apply", data.Events.Last());
            run.Dispose();
        }

        Stop(runtime, module);
    }

    /// <summary>Cycles, cross-builder nodes, and empty graphs fail before submission.</summary>
    [Fact]
    public void InvalidGraphDefinitionsAreRejected()
    {
        var module = new GraphModule();
        _ = new ExecutionRuntime([module]);
        var builder = new JobGraphBuilder<GraphInput>();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        JobNodeId a = builder.Add("a", module.Prepare);
        JobNodeId b = builder.Add("b", module.Apply);
        builder.DependsOn(a, b);
        builder.DependsOn(b, a);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        var other = new JobGraphBuilder<GraphInput>();
        JobNodeId c = other.Add("c", module.Prepare);
        Assert.Throws<ArgumentException>(() => builder.DependsOn(a, c));
    }

    /// <summary>Token cancellation wakes gate-blocked CPU work without executing its handler on Host.</summary>
    [Fact]
    public void CancellationWakesGatedThreadPoolCommand()
    {
        var module = new GraphModule { GatedCpu = true };
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        var data = new GraphData(1);
        var input = new GraphInput(data);
        using var cancellation = new CancellationTokenSource();
        ValueTask<Unit> result = runtime.Bind(module.Prepare).ExecuteAsync(input, cancellation.Token);
        Assert.False(result.IsCompleted);
        cancellation.Cancel();
        Assert.True(SpinWait.SpinUntil(() => result.IsCompleted, TimeSpan.FromSeconds(5)));
        Assert.Throws<OperationCanceledException>(() => CompletedResult.Read(result));
        Assert.Empty(data.Events);
        Stop(runtime, module);
    }

    private static JobGraph<GraphInput> Build(GraphModule module)
    {
        var builder = new JobGraphBuilder<GraphInput>();
        JobNodeId prepare = builder.Add("Prepare", module.Prepare);
        JobNodeId range = builder.AddRange("Range", module.Range, 8);
        JobNodeId apply = builder.Add("Apply", module.Apply);
        builder.DependsOn(range, prepare);
        builder.DependsOn(apply, range);
        return builder.Build();
    }

    private static void Stop(ExecutionRuntime runtime, GraphModule module)
    {
        Task stop = runtime.StopAsync().AsTask();
        PumpUntil(runtime, module, () => stop.IsCompleted);
        CompletedResult.Read(stop);
    }

    private static void PumpUntil(ExecutionRuntime runtime, GraphModule module, Func<bool> done)
    {
        long start = Stopwatch.GetTimestamp();
        while (!done())
        {
            runtime.PumpHostLifecycle(8, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(8, TimeSpan.FromMilliseconds(2));
            if (!module.Phase.Equals(default(PhaseKey)))
            {
                runtime.RunHostPhase(module.Phase, 8, TimeSpan.FromMilliseconds(2));
            }

            if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("Graph did not complete.");
            }

            Thread.Sleep(1);
        }
    }
}
