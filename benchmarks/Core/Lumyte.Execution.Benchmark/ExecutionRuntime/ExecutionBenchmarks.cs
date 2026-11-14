using BenchmarkDotNet.Attributes;

namespace Lumyte.Execution.Benchmark;

/// <summary>Measures bounded cooperative command, graph, range, and workflow execution.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Execution", "Screening", "Comparison", "Diagnostic")]
public class ExecutionBenchmarks
{
    private Fixture _fixture = null!;
    private JobGraph<Input> _graph = null!;
    private JobGraph<Input> _range = null!;
    private Input _input;

    /// <summary>Gets or sets the finite workload size.</summary>
    [Params(1, 16, 256)]
    public int Count { get; set; } = 16;

    /// <summary>Creates fresh endpoints and reusable graphs outside measurements.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fixture = new Fixture();
        _input = new Input(new int[Count]);
        _graph = BuildGraph();
        var builder = new JobGraphBuilder<Input>();
        builder.AddRange("range", _fixture.Module.Range, 64);
        _range = builder.Build();
    }

    /// <summary>Stops the runtime after all results have been consumed.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    /// <summary>Submits, delivers on the fixed host, and observes one result.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Commands")]
    public int Single() => _fixture.Read(_fixture.Module.Port.ExecuteAsync(7));

    /// <summary>Stages, accepts, observes, disposes, and resets one whole Count-command batch; one invocation is one batch, not one command.</summary>
    /// <returns>The last typed command result, equal to twice Count minus two.</returns>
    [Benchmark]
    [BenchmarkCategory("Batches")]
    public int Batch()
    {
        using var batch = new CommandBatch(Count);
        CommandResultKey<int> last = default;
        for (int i = 0; i < Count; i++)
        {
            last = batch.Add(_fixture.Module.Port, i);
        }

        Require(_fixture.Runtime.Submit(batch, out CommandRun? run) == CommandEnqueueStatus.Accepted);
        _fixture.Until(() => run!.IsCompleted);
        run!.ThrowIfFailed();
        int result = run.GetResult(last);
        run.Dispose();
        batch.Reset();
        return result;
    }

    /// <summary>Builds an immutable command dependency chain without scheduling it.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Graphs")]
    public JobGraph<Input> BuildGraph()
    {
        var builder = new JobGraphBuilder<Input>();
        JobNodeId previous = default;
        for (int i = 0; i < Count; i++)
        {
            JobNodeId current = builder.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture), _fixture.Module.GraphWork);
            if (i > 0)
            {
                builder.DependsOn(current, previous);
            }

            previous = current;
        }

        return builder.Build();
    }

    /// <summary>Schedules a prepared chain and consumes the actual node side effects.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Graphs")]
    public int Graph()
    {
        Array.Clear(_input.Values);
        Run(_graph);
        return _input.Values[0];
    }

    /// <summary>Schedules disjoint chunks and observes every written element.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Ranges")]
    public int Range()
    {
        Array.Clear(_input.Values);
        Run(_range);
        return _input.Values.Sum();
    }

    /// <summary>Runs a synchronous workflow entry to actual result observation.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Workflows")]
    public int Workflow() => _fixture.Read(_fixture.Module.Workflow.ExecuteAsync(7));

    /// <summary>Suspends a workflow once and resumes through its actual cooperative context.</summary>
    /// <returns>The resumed workflow result.</returns>
    [Benchmark]
    [BenchmarkCategory("Workflows")]
    public int SuspendedWorkflow()
    {
        var resume = new TaskCompletionSource();
        _fixture.Module.Resume = resume;
        ValueTask<int> result = _fixture.Module.Workflow.ExecuteAsync(42);
        _fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        if (result.IsCompleted)
        {
            throw new InvalidOperationException("Workflow did not suspend.");
        }

        resume.SetResult();
        return _fixture.Read(result);
    }

    /// <summary>Awaits a child command on the same runtime with one cooperative worker.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Workflows")]
    public int ChildWorkflow() => _fixture.Read(_fixture.Module.Workflow.ExecuteAsync(-7));

    /// <summary>Runs a bounded host turn containing commands and a real update.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("HostPhases")]
    public int HostTurn()
    {
        var pending = new ValueTask<int>[Count];
        for (int i = 0; i < Count; i++)
        {
            pending[i] = _fixture.Module.Port.ExecuteAsync(i);
        }

        int total = 0;
        foreach (ValueTask<int> result in pending)
        {
            total += _fixture.Read(result);
        }

        return total;
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Execution contract failed.");
        }
    }

    private void Run(JobGraph<Input> graph)
    {
        Require(_fixture.Runtime.Schedule(graph, in _input, out JobRun? run) == CommandEnqueueStatus.Accepted);
        _fixture.Until(() => run!.IsCompleted);
        run!.ThrowIfFailed();
        run.Dispose();
    }

    /// <summary>The finite graph payload.</summary>
    /// <param name="Values">The overwritten result buffer.</param>
    public readonly record struct Input(int[] Values);
}
