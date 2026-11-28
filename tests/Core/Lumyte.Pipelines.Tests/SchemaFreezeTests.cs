using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Checks immutable schema capture before processor ownership is created.</summary>
public sealed class SchemaFreezeTests
{
    /// <summary>Changing a schema after Complete rejects Build without calling Create.</summary>
    [Fact]
    public void ChangedSchemaRejectedBeforeProcessorCreation()
    {
        Mutable.Descriptor = Make(1);
        var stage = new Producer<Mutable>();
        var graph = new PipelineGraph<int>();
        PipelineDefinition<int, Host<Mutable>> definition = graph.Complete(graph.Stage(stage, graph.Input));
        Mutable.Descriptor = Make(2);
        try
        {
            using var host = new PipelineBuildHost(new ExecutionRuntime([]), new PipelineBudget(8, 4096, 1));
            Assert.Throws<InvalidOperationException>(() => host.Build(definition));
            Assert.Equal(0, stage.Creates);
        }
        finally
        {
            Mutable.Descriptor = Make(1);
        }
    }

    /// <summary>Inspection names the captured declaration implementation and canonical layout, not a private factory.</summary>
    [Fact]
    public void DescriptionContainsCapturedSchemaAndImplementation()
    {
        InspectionSchema.Descriptor = Make(1);
        var graph = new PipelineGraph<int>();
        var stage = new Producer<InspectionSchema>();
        PipelineDescription description = graph.Complete(graph.Stage(stage, graph.Input)).DescribeDataflow();
        PipelineNodeDescription node = Assert.Single(description.Nodes);
        Assert.Equal(typeof(Producer<InspectionSchema>), node.ImplementationType);
        Assert.Equal("Host", node.Placement);
        Assert.Equal(1, node.Schema!.Version);
        Assert.Equal(typeof(float), node.Schema.ElementType);
        Assert.Equal(4, node.Schema.ElementStride);
    }

    /// <summary>Already-captured canonical bytes cannot be reinterpreted by changing static schema layout.</summary>
    [Fact]
    public void RetainedCanonicalDataUsesCapturedLayout()
    {
        RetainedSchema.Descriptor = Make(1);
        SchemaData<RetainedSchema>? retained = null;
        var producer = new Producer<RetainedSchema> { Capture = data => retained = data };
        var graph = new PipelineGraph<int>();
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, 4096, 1));
        using IPipeline<int, Host<RetainedSchema>> pipeline = host.Build(graph.Complete(graph.Input.Then(producer)));
        Task<PipelineResult<Host<RetainedSchema>>> pending = pipeline.PrepareAsync(1).AsTask();
        Pump(runtime, pending);
        using PipelineResult<Host<RetainedSchema>> result = Completed(pending);
        try
        {
            RetainedSchema.Descriptor = new SchemaDescriptor(2, typeof(int), 4, new Dictionary<string, object>());
            Assert.Throws<InvalidOperationException>(() => retained!.ReadElements<int>().ToArray());
            Assert.Equal(1f, retained!.ReadElements<float>()[0]);
            Assert.Equal(typeof(float), result.Value.Descriptor.ElementType);
            Assert.Equal(1f, result.Value.ReadElements<float>()[0]);
            Assert.Throws<InvalidOperationException>(() => result.Value.ReadElements<int>().ToArray());
        }
        finally
        {
            RetainedSchema.Descriptor = Make(1);
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static T Completed<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static void Pump(ExecutionRuntime runtime, Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (timer.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private static SchemaDescriptor Make(int version) => new(version, typeof(float), 4, new Dictionary<string, object>());

    private sealed class Mutable : ISchema
    {
        public static SchemaDescriptor Descriptor { get; set; } = Make(1);
    }

    private sealed class InspectionSchema : ISchema
    {
        public static SchemaDescriptor Descriptor { get; set; } = Make(1);
    }

    private sealed class RetainedSchema : ISchema
    {
        public static SchemaDescriptor Descriptor { get; set; } = Make(1);
    }

    private sealed class Producer<TSchema> : IStage<int, Host<TSchema>>
        where TSchema : ISchema
    {
        internal Producer()
        {
            Factory = new FactoryImpl(this);
        }

        public IStageFactory<int, Host<TSchema>> Factory { get; }

        internal int Creates { get; private set; }

        internal Action<SchemaData<TSchema>>? Capture { get; init; }

        private sealed class FactoryImpl(Producer<TSchema> owner) : IStageFactory<int, Host<TSchema>>, IStageProcessor<int, Host<TSchema>>
        {
            public StageRequirements Describe(StageInputDescriptions inputs) => new(4096, []);

            public IStageProcessor<int, Host<TSchema>> Create(StageBuildContext context)
            {
                owner.Creates++;
                return this;
            }

            public ValueTask<ValueLease<Host<TSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
            {
                SchemaData<TSchema> data = context.CopyHostData<TSchema, float>([1], new SchemaExtent(1, new Dictionary<string, object>()));
                owner.Capture?.Invoke(data);
                return ValueTask.FromResult(context.PublishHost(data));
            }

            public void Dispose()
            {
            }
        }
    }
}
