using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Executes externally implemented factory/processor overloads.</summary>
public sealed class ArityTests
{
    /// <summary>Every multi-input public overload accepts and executes external processors.</summary>
    /// <param name="arity">The number of typed input edges.</param>
    /// <returns>The completion of the externally implemented pipeline.</returns>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ExternalProcessorsExecuteAllAritiesAsync(int arity)
    {
        var graph = new PipelineGraph<int>();
        Port<int> p = graph.Input;
        Port<int> output = arity switch
        {
            3 => graph.Stage(new Sum3(), p, p, p),
            4 => graph.Stage(new Sum4(), p, p, p, p),
            5 => graph.Stage(new Sum5(), p, p, p, p, p),
            6 => graph.Stage(new Sum6(), p, p, p, p, p, p),
            _ => throw new ArgumentOutOfRangeException(nameof(arity)),
        };
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, 4096, 1));
        using IPipeline<int, int> pipeline = host.Build(graph.Complete(output));
        Task<PipelineResult<int>> pending = pipeline.PrepareAsync(2).AsTask();
        Pump(runtime, pending);
        using PipelineResult<int> result = await pending;
        Assert.Equal(arity * 2, result.Value);
        Pump(runtime, runtime.StopAsync().AsTask());
    }

    private static void Pump(ExecutionRuntime runtime, Task task)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private sealed class Sum3 : IStage<int, int, int, int>, IStageFactory<int, int, int, int>, IStageProcessor<int, int, int, int>
    {
        public IStageFactory<int, int, int, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(68, []);

        public IStageProcessor<int, int, int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, ValueLease<int> c, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b) + context.Read(c)));

        public void Dispose()
        {
        }
    }

    private sealed class Sum4 : IStage<int, int, int, int, int>, IStageFactory<int, int, int, int, int>, IStageProcessor<int, int, int, int, int>
    {
        public IStageFactory<int, int, int, int, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(68, []);

        public IStageProcessor<int, int, int, int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, ValueLease<int> c, ValueLease<int> d, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b) + context.Read(c) + context.Read(d)));

        public void Dispose()
        {
        }
    }

    private sealed class Sum5 : IStage<int, int, int, int, int, int>, IStageFactory<int, int, int, int, int, int>, IStageProcessor<int, int, int, int, int, int>
    {
        public IStageFactory<int, int, int, int, int, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(68, []);

        public IStageProcessor<int, int, int, int, int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, ValueLease<int> c, ValueLease<int> d, ValueLease<int> e, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b) + context.Read(c) + context.Read(d) + context.Read(e)));

        public void Dispose()
        {
        }
    }

    private sealed class Sum6 : IStage<int, int, int, int, int, int, int>, IStageFactory<int, int, int, int, int, int, int>, IStageProcessor<int, int, int, int, int, int, int>
    {
        public IStageFactory<int, int, int, int, int, int, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(68, []);

        public IStageProcessor<int, int, int, int, int, int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, ValueLease<int> c, ValueLease<int> d, ValueLease<int> e, ValueLease<int> f, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b) + context.Read(c) + context.Read(d) + context.Read(e) + context.Read(f)));

        public void Dispose()
        {
        }
    }
}
