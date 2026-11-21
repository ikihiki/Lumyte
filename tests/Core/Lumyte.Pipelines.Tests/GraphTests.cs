using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Exercises declarations and real existing-runtime lowering.</summary>
public sealed class GraphTests
{
    /// <summary>Shared producers execute once before fan-in.</summary>
    [Fact]
    public void SharedProducerExecutesOnce()
    {
        var g = new PipelineGraph<int>();
        var stage = new Increment();
        Port<int> first = g.Stage(stage, g.Input);
        Port<int> second = first.Then(stage);
        Port<int> output = g.Stage(new Sum(), first, second);
        PipelineDefinition<int, int> definition = g.Complete(output);
        Assert.Same(definition.DescribeDataflow(), definition.DescribeDataflow());
        Execute(definition, 4, result => Assert.Equal(11, result.Value));
        Assert.Equal(2, stage.Calls);
    }

    /// <summary>Freeze rejects both Stage and Then.</summary>
    [Fact]
    public void CompleteFreezesGraph()
    {
        var g = new PipelineGraph<int>();
        g.Complete(g.Input);
        Assert.Throws<InvalidOperationException>(() => g.Stage(new Increment(), g.Input));
        Assert.Throws<InvalidOperationException>(() => g.Input.Then(new Increment()));
    }

    /// <summary>Ports cannot cross graph identities.</summary>
    [Fact]
    public void ForeignPortRejected()
    {
        var g = new PipelineGraph<int>();
        var other = new PipelineGraph<int>();
        Assert.Throws<ArgumentException>(() => g.Stage(new Increment(), other.Input));
        Assert.Throws<ArgumentException>(() => g.Complete(other.Input));
    }

    /// <summary>Identity definitions accept arbitrary input and output.</summary>
    [Fact]
    public void InputCanBeOutput()
    {
        var g = new PipelineGraph<int>();
        Execute(g.Complete(g.Input), 7, result => Assert.Equal(7, result.Value));
    }

    /// <summary>Finite requirements are checked before processor creation.</summary>
    [Fact]
    public void BudgetRejectedBeforeCreation()
    {
        var runtime = new ExecutionRuntime([]);
        var g = new PipelineGraph<int>();
        var stage = new Increment();
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, 0, 1));
        Assert.Throws<InvalidOperationException>(() => host.Build(g.Complete(g.Stage(stage, g.Input))));
        Assert.Equal(0, stage.Creates);
    }

    /// <summary>Results independently retain processors after pipeline disposal.</summary>
    [Fact]
    public void ResultRetainsProcessor()
    {
        var g = new PipelineGraph<int>();
        var stage = new Increment();
        Execute(
            g.Complete(g.Stage(stage, g.Input)),
            1,
            result =>
            {
                Assert.Equal(0, stage.Disposals);
                Assert.Equal(2, result.Value);
            },
            disposePipelineFirst: true);
        Assert.Equal(1, stage.Disposals);
    }

    /// <summary>Compiled definitions can be built independently more than once.</summary>
    [Fact]
    public void DefinitionSupportsIndependentBuilds()
    {
        var g = new PipelineGraph<int>();
        PipelineDefinition<int, int> definition = g.Complete(g.Input);
        Execute(definition, 3, result => Assert.Equal(3, result.Value));
        Execute(definition, 9, result => Assert.Equal(9, result.Value));
    }

    /// <summary>Schema metadata is copied rather than borrowed from mutable dictionaries.</summary>
    [Fact]
    public void SchemaMetadataIsCaptured()
    {
        var metadata = new Dictionary<string, object> { ["version"] = 1 };
        var descriptor = new SchemaDescriptor(1, typeof(float), 4, metadata);
        var extent = new SchemaExtent(2, metadata);
        metadata["version"] = 7;
        Assert.Equal(1, descriptor.Metadata["version"]);
        Assert.Equal(1, extent.Metadata["version"]);
        Assert.Throws<ArgumentException>(() => new SchemaExtent(0, new Dictionary<string, object> { ["mutable"] = new int[1] }));
    }

    /// <summary>Invalid finite limits are rejected immediately.</summary>
    [Fact]
    public void NegativeBudgetsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineBudget(0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineBudget(1, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineBudget(1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StageRequirements(-1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaExtent(-1, new Dictionary<string, object>()));
    }

    /// <summary>No Graphics or Drawing2D dependency leaks into the core assembly.</summary>
    [Fact]
    public void CoreHasNoGraphicsReference()
        => Assert.DoesNotContain(typeof(PipelineGraph<>).Assembly.GetReferencedAssemblies(), reference => reference.Name!.Contains("Graphics", StringComparison.Ordinal));

    /// <summary>Async processors resume through the existing runtime workflow queue.</summary>
    [Fact]
    public void AsyncStageResumesBeforeConsumer()
    {
        var g = new PipelineGraph<int>();
        var stage = new Increment { Yield = true };
        Port<int> first = g.Stage(stage, g.Input);
        Execute(g.Complete(first.Then(stage)), 5, result => Assert.Equal(7, result.Value));
        Assert.Equal(2, stage.Resumptions);
    }

    private static void Execute(PipelineDefinition<int, int> definition, int input, Action<PipelineResult<int>> inspect, bool disposePipelineFirst = false)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Task start = runtime.StartAsync().AsTask();
        Pump(runtime, start);
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(16, 4096, 2));
        using IPipeline<int, int> pipeline = host.Build(definition);
        Task<PipelineResult<int>> pending = pipeline.PrepareAsync(input).AsTask();
        Pump(runtime, pending);
        using (PipelineResult<int> result = pending.GetAwaiter().GetResult())
        {
            if (disposePipelineFirst)
            {
                pipeline.Dispose();
            }

            inspect(result);
        }

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

    private sealed class Increment : IStage<int, int>, IStageFactory<int, int>, IStageProcessor<int, int>
    {
        public IStageFactory<int, int> Factory => this;

        internal int Calls { get; private set; }

        internal int Creates { get; private set; }

        internal int Disposals { get; private set; }

        internal bool Yield { get; init; }

        internal int Resumptions { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(68, []);

        public IStageProcessor<int, int> Create(StageBuildContext context)
        {
            Creates++;
            return this;
        }

        public async ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
        {
            Calls++;
            if (Yield)
            {
                await Task.Yield();
                Assert.NotNull(SynchronizationContext.Current);
                Resumptions++;
            }

            return context.Publish(context.Read(a) + 1);
        }

        public void Dispose() => Disposals++;
    }

    private sealed class Sum : IStage<int, int, int>, IStageFactory<int, int, int>, IStageProcessor<int, int, int>
    {
        public IStageFactory<int, int, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(68, []);

        public IStageProcessor<int, int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b)));

        public void Dispose()
        {
        }
    }
}
