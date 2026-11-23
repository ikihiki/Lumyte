using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Exercises actual-input preflight and canonical publication provenance.</summary>
public sealed class CanonicalTests
{
    /// <summary>A passthrough ceiling is checked before invoking its processor.</summary>
    [Fact]
    public void InputElementCeilingIsPreflighted()
    {
        var graph = new PipelineGraph<int>();
        var pass = new Pass(1);
        Port<Host<Floats>> values = graph.Stage(new Producer(), graph.Input);
        PipelineDefinition<int, Host<Floats>> definition = graph.Complete(values.Then(pass));
        WithPipeline(definition, (runtime, pipeline) =>
        {
            Task<PipelineResult<Host<Floats>>> pending = pipeline.PrepareAsync(2).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, pending));
            Assert.Equal(0, pass.Calls);
        });
    }

    /// <summary>Describe receives build-time bounds and real per-run input extents.</summary>
    [Fact]
    public void InputDependentRequirementsReceiveExtents()
    {
        var graph = new PipelineGraph<int>();
        var pass = new Pass(8, dependent: true);
        Port<Host<Floats>> values = graph.Stage(new Producer(), graph.Input);
        WithPipeline(graph.Complete(values.Then(pass)), (runtime, pipeline) =>
        {
            Task<PipelineResult<Host<Floats>>> pending = pipeline.PrepareAsync(2).AsTask();
            Pump(runtime, pending);
            using PipelineResult<Host<Floats>> result = pending.Result;
            Assert.Equal(1, pass.Calls);
            Assert.Equal(2, pass.LastActualElements);
            Assert.True(pass.SawUpperBound);
        });
    }

    /// <summary>Generic publication cannot publish an uninitialized canonical placement.</summary>
    [Fact]
    public void DefaultHostPublicationRejected()
    {
        var graph = new PipelineGraph<int>();
        WithPipeline(graph.Complete(graph.Stage(new Producer(invalid: true), graph.Input)), (runtime, pipeline) =>
        {
            Task<PipelineResult<Host<Floats>>> pending = pipeline.PrepareAsync(2).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, pending));
        });
    }

    /// <summary>Generic publication cannot reuse a payload captured in another invocation.</summary>
    [Fact]
    public void CapturedHostCannotBeRepublished()
    {
        var graph = new PipelineGraph<int>();
        var pass = new Pass(8, cache: true);
        Port<Host<Floats>> values = graph.Stage(new Producer(), graph.Input);
        WithPipeline(graph.Complete(values.Then(pass)), (runtime, pipeline) =>
        {
            Task<PipelineResult<Host<Floats>>> first = pipeline.PrepareAsync(2).AsTask();
            Pump(runtime, first);
            first.Result.Dispose();
            Task<PipelineResult<Host<Floats>>> second = pipeline.PrepareAsync(2).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, second));
        });
    }

    private static void WithPipeline(PipelineDefinition<int, Host<Floats>> definition, Action<ExecutionRuntime, IPipeline<int, Host<Floats>>> action)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        using var build = new PipelineBuildHost(runtime, new PipelineBudget(16, 4096, 1));
        using IPipeline<int, Host<Floats>> pipeline = build.Build(definition);
        try
        {
            action(runtime, pipeline);
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
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

    private sealed class Floats : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(float), 4, new Dictionary<string, object>());
    }

    private sealed class Producer(bool invalid = false) : IStage<int, Host<Floats>>, IStageFactory<int, Host<Floats>>, IStageProcessor<int, Host<Floats>>
    {
        public IStageFactory<int, Host<Floats>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(216, []);

        public IStageProcessor<int, Host<Floats>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<Floats>>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
        {
            if (invalid)
            {
                return ValueTask.FromResult(context.Publish(default(Host<Floats>)));
            }

            SchemaData<Floats> data = context.CopyHostData<Floats, float>([1, 2], new SchemaExtent(2, new Dictionary<string, object>()));
            return ValueTask.FromResult(context.PublishHost(data));
        }

        public void Dispose()
        {
        }
    }

    private sealed class Pass(long limit, bool dependent = false, bool cache = false) : IStage<Host<Floats>, Host<Floats>>, IStageFactory<Host<Floats>, Host<Floats>>, IStageProcessor<Host<Floats>, Host<Floats>>
    {
        private Host<Floats> _cached;

        public IStageFactory<Host<Floats>, Host<Floats>> Factory => this;

        internal int Calls { get; private set; }

        internal long LastActualElements { get; private set; }

        internal bool SawUpperBound { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            if (!dependent)
            {
                return Requirements.HostPassthrough<Floats>(limit);
            }

            SchemaExtent extent = inputs.Extents[0];
            if (extent.Metadata.ContainsKey("UpperBound"))
            {
                SawUpperBound = true;
            }
            else
            {
                LastActualElements = extent.ElementCount;
            }

            return new StageRequirements(checked((extent.ElementCount * 4) + 200), []);
        }

        public IStageProcessor<Host<Floats>, Host<Floats>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<Floats>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<Floats>> a, CancellationToken cancellationToken)
        {
            Calls++;
            if (cache && Calls > 1)
            {
                return ValueTask.FromResult(context.Publish(_cached));
            }

            _cached = context.Read(a);
            return ValueTask.FromResult(context.Forward(a));
        }

        public void Dispose()
        {
        }
    }
}
