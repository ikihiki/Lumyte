using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Finite application metadata must not be treated as a scalar.</summary>
public sealed class ApplicationInputBudgetTests
{
    /// <summary>Ten elements fit the actual finite host reservation.</summary>
    [Fact]
    public void TenElementApplicationInputFitsFiniteBudget()
    {
        var stage = new CountStage();
        var graph = new PipelineGraph<ApplicationInput>();
        PipelineDefinition<ApplicationInput, int> definition = graph.Complete(graph.Input.Then(stage));
        Execute(definition, new ApplicationInput(10), new PipelineBudget(8, 108, 1, maxInputElements: 10), (runtime, pipeline, input) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(input).AsTask();
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.GetAwaiter().GetResult();
            Assert.Equal(10, result.Value);
            Assert.Equal(1, stage.Invocations);
        });
    }

    /// <summary>Element and byte limits reject input before its processor can allocate.</summary>
    /// <param name="count">The actual input count.</param>
    /// <param name="bytes">The finite run byte limit.</param>
    [Theory]
    [InlineData(11, 108)]
    [InlineData(11, 1000)]
    [InlineData(10, 107)]
    public void RejectedInputDoesNotInvokeProcessor(long count, long bytes)
    {
        var stage = new CountStage();
        var graph = new PipelineGraph<ApplicationInput>();
        Execute(graph.Complete(graph.Input.Then(stage)), new ApplicationInput(count), new PipelineBudget(8, bytes, 1, 10), (runtime, pipeline, input) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(input).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, pending));
            Assert.Equal(0, stage.Invocations);
        });
    }

    /// <summary>Missing application metadata ceilings fail before processor creation.</summary>
    [Fact]
    public void MissingApplicationCeilingFailsBuildBeforeCreate()
    {
        var graph = new PipelineGraph<ApplicationInput>();
        var stage = new CountStage();
        using var host = new PipelineBuildHost(new ExecutionRuntime([]), new PipelineBudget(8, 108, 1));
        Assert.Throws<InvalidOperationException>(() => host.Build(graph.Complete(graph.Input.Then(stage))));
        Assert.Equal(0, stage.Creates);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineBudget(8, 108, 1, -1));
    }

    /// <summary>Zero is a valid finite application input extent.</summary>
    [Fact]
    public void EmptyApplicationInputFitsZeroElementCeiling()
    {
        var graph = new PipelineGraph<ApplicationInput>();
        Execute(graph.Complete(graph.Input.Then(new CountStage())), new ApplicationInput(0), new PipelineBudget(8, 68, 1, 0), (runtime, pipeline, input) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(input).AsTask();
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.GetAwaiter().GetResult();
            Assert.Equal(0, result.Value);
        });
    }

    /// <summary>Flattened templates and mixed scalar/application edges use their own input extents.</summary>
    [Fact]
    public void TemplateAndMultiInputKeepApplicationCeilingAndScalarExtent()
    {
        var template = new PipelineGraph<ApplicationInput>();
        PipelineDefinition<ApplicationInput, int> definition = template.Complete(template.Input.Then(new CountStage()));
        var graph = new PipelineGraph<ApplicationInput>();
        Port<int> count = graph.Invoke(definition, graph.Input);
        Port<int> output = graph.Stage(new MixedStage(), graph.Input, count);
        Execute(graph.Complete(output), new ApplicationInput(10), new PipelineBudget(8, 220, 1, 10), (runtime, pipeline, input) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(input).AsTask();
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.GetAwaiter().GetResult();
            Assert.Equal(20, result.Value);
        });
    }

    /// <summary>Downstream build and validation use the producer ceiling for synchronous and asynchronous output.</summary>
    /// <param name="asynchronous">Whether publication completes asynchronously.</param>
    /// <param name="ceiling">The declared output element ceiling.</param>
    [Theory]
    [InlineData(false, 9)]
    [InlineData(true, 9)]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    public void OutputExtentIsValidatedBeforeDownstreamInvocation(bool asynchronous, long ceiling)
    {
        var graph = new PipelineGraph<ApplicationInput>();
        var count = new CountStage();
        Port<ApplicationInput> forwarded = graph.Input.Then(new ForwardStage(asynchronous, ceiling));
        Execute(graph.Complete(forwarded.Then(count)), new ApplicationInput(10), new PipelineBudget(8, 512, 1, 10), (runtime, pipeline, input) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(input).AsTask();
            if (ceiling < input.Count)
            {
                Assert.Throws<InvalidOperationException>(() => Pump(runtime, pending));
                Assert.Equal(0, count.Invocations);
            }
            else
            {
                Pump(runtime, pending);
                using PipelineResult<int> result = pending.GetAwaiter().GetResult();
                Assert.Equal(10, result.Value);
                Assert.Equal(1, count.Invocations);
            }
        });
    }

    private static void Execute<TInput>(PipelineDefinition<TInput, int> definition, TInput input, PipelineBudget budget, Action<ExecutionRuntime, IPipeline<TInput, int>, TInput> action)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            using var host = new PipelineBuildHost(runtime, budget);
            using IPipeline<TInput, int> pipeline = host.Build(definition);
            action(runtime, pipeline, input);
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static void Pump(ExecutionRuntime runtime, Task pending)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!pending.IsCompleted)
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            Thread.Yield();
        }

        pending.GetAwaiter().GetResult();
    }

    private sealed record ApplicationInput(long Count) : IImmutablePipelineInput, ICpuPipelineValue
    {
        public SchemaExtent Description => new(Count, new Dictionary<string, object>());
    }

    private sealed class ForwardStage(bool asynchronous, long ceiling) : IStage<ApplicationInput, ApplicationInput>, IStageFactory<ApplicationInput, ApplicationInput>, IStageProcessor<ApplicationInput, ApplicationInput>
    {
        public IStageFactory<ApplicationInput, ApplicationInput> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []) { OutputMaxElements = ceiling };

        public IStageProcessor<ApplicationInput, ApplicationInput> Create(StageBuildContext context) => this;

        public async ValueTask<ValueLease<ApplicationInput>> PrepareAsync(StagePreparationContext context, ValueLease<ApplicationInput> a, CancellationToken cancellationToken)
        {
            if (asynchronous)
            {
                await Task.Yield();
            }

            return context.Forward(a);
        }

        public void Dispose()
        {
        }
    }

    private sealed class MixedStage : IStage<ApplicationInput, int, int>, IStageFactory<ApplicationInput, int, int>, IStageProcessor<ApplicationInput, int, int>
    {
        public IStageFactory<ApplicationInput, int, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            Assert.Equal(1, inputs.Extents[1].ElementCount);
            return new StageRequirements(checked(68 + (4 * inputs.Extents[0].ElementCount)), []);
        }

        public IStageProcessor<ApplicationInput, int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<ApplicationInput> a, ValueLease<int> b, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish((int)context.Read(a).Count + context.Read(b)));

        public void Dispose()
        {
        }
    }

    private sealed class CountStage : IStage<ApplicationInput, int>, IStageFactory<ApplicationInput, int>, IStageProcessor<ApplicationInput, int>
    {
        public IStageFactory<ApplicationInput, int> Factory => this;

        internal int Invocations { get; private set; }

        internal int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(checked(68 + (4 * inputs.Extents[0].ElementCount)), []);

        public IStageProcessor<ApplicationInput, int> Create(StageBuildContext context)
        {
            Creates++;
            return this;
        }

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<ApplicationInput> a, CancellationToken cancellationToken)
        {
            Invocations++;
            return ValueTask.FromResult(context.Publish((int)context.Read(a).Count));
        }

        public void Dispose()
        {
        }
    }
}
