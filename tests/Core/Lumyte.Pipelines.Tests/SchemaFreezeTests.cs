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
        var stage = new Producer();
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
        Mutable.Descriptor = Make(1);
        var graph = new PipelineGraph<int>();
        var stage = new Producer();
        PipelineDescription description = graph.Complete(graph.Stage(stage, graph.Input)).DescribeDataflow();
        PipelineNodeDescription node = Assert.Single(description.Nodes);
        Assert.Equal(typeof(Producer), node.ImplementationType);
        Assert.Equal("Host", node.Placement);
        Assert.Equal(1, node.Schema!.Version);
        Assert.Equal(typeof(float), node.Schema.ElementType);
        Assert.Equal(4, node.Schema.ElementStride);
    }

    private static SchemaDescriptor Make(int version) => new(version, typeof(float), 4, new Dictionary<string, object>());

    private sealed class Mutable : ISchema
    {
        public static SchemaDescriptor Descriptor { get; set; } = Make(1);
    }

    private sealed class Producer : IStage<int, Host<Mutable>>
    {
        internal Producer()
        {
            Factory = new FactoryImpl(this);
        }

        public IStageFactory<int, Host<Mutable>> Factory { get; }

        internal int Creates { get; private set; }

        private sealed class FactoryImpl(Producer owner) : IStageFactory<int, Host<Mutable>>, IStageProcessor<int, Host<Mutable>>
        {
            public StageRequirements Describe(StageInputDescriptions inputs) => new(4096, []);

            public IStageProcessor<int, Host<Mutable>> Create(StageBuildContext context)
            {
                owner.Creates++;
                return this;
            }

            public ValueTask<ValueLease<Host<Mutable>>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
            {
                SchemaData<Mutable> data = context.CopyHostData<Mutable, float>([1], new SchemaExtent(1, new Dictionary<string, object>()));
                return ValueTask.FromResult(context.PublishHost(data));
            }

            public void Dispose()
            {
            }
        }
    }
}
