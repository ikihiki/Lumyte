using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks the genuine generic Core scope cannot bypass frame provenance or nested opaque-value validation.</summary>
public sealed class BackendScopeGuardTests
{
    /// <summary>Every child of an opaque typed bundle is checked before generic backend reads return.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task GenericScopeRejectsForeignNestedReadAsync()
    {
        await using var host = new PlanningHost();
        await using var firstSession = new SessionOwner(host.Module, new ManagedDevice());
        await using var secondSession = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(firstSession.CreateFrame());
        await using var foreignFrame = new FrameOwner(secondSession.CreateFrame());
        Device<BufferSchema> own = Value(frame);
        Device<BufferSchema> foreign = Value(foreignFrame);
        var stage = new BundleReadStage();
        var graph = new PipelineGraph<PipelineBundle<Device<BufferSchema>, PipelineBundle<int, Device<BufferSchema>>>>();
        using var build = new PipelineBuildHost(host.Runtime, new(8, 65536, 1), new GraphicsPipelineBackend(frame.Value));
        using IPipeline<PipelineBundle<Device<BufferSchema>, PipelineBundle<int, Device<BufferSchema>>>, int> pipeline = build.Build(graph.Complete(graph.Input.Then(stage)));
        PipelineResult<int>? result = null;
        Exception? failure = await Record.ExceptionAsync(async () => result = await pipeline.PrepareAsync(new(own, new(7, foreign))));
        result?.Dispose();
        Assert.IsType<ArgumentException>(failure);
        Assert.Contains("Foreign resource", failure!.Message, StringComparison.Ordinal);
        Assert.False(stage.ReadReturned);
    }

    /// <summary>Generic backend publication validates foreign opaque values before output ownership can escape.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task GenericScopeRejectsForeignPublicationAsync()
    {
        await using var host = new PlanningHost();
        await using var firstSession = new SessionOwner(host.Module, new ManagedDevice());
        await using var secondSession = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(firstSession.CreateFrame());
        await using var foreignFrame = new FrameOwner(secondSession.CreateFrame());
        var stage = new ForeignPublishStage(Value(foreignFrame));
        var graph = new PipelineGraph<Device<BufferSchema>>();
        using var build = new PipelineBuildHost(host.Runtime, new(8, 65536, 1), new GraphicsPipelineBackend(frame.Value));
        using IPipeline<Device<BufferSchema>, Device<BufferSchema>> pipeline = build.Build(graph.Complete(graph.Input.Then(stage)));
        PipelineResult<Device<BufferSchema>>? result = null;
        Exception? failure = await Record.ExceptionAsync(async () => result = await pipeline.PrepareAsync(Value(frame)));
        result?.Dispose();
        Assert.IsType<ArgumentException>(failure);
        Assert.Contains("Foreign resource", failure!.Message, StringComparison.Ordinal);
    }

    private static Device<BufferSchema> Value(FrameOwner frame)
    {
        PlanBuffer<int> buffer = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        return frame.Plan.BufferValue<BufferSchema, int>(buffer, new(4, new Dictionary<string, object>()));
    }

    private sealed class BufferSchema : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(int), sizeof(int), new Dictionary<string, object>());
    }

    private sealed class BundleReadStage : IStage<PipelineBundle<Device<BufferSchema>, PipelineBundle<int, Device<BufferSchema>>>, int>, IStageFactory<PipelineBundle<Device<BufferSchema>, PipelineBundle<int, Device<BufferSchema>>>, int>
    {
        public IStageFactory<PipelineBundle<Device<BufferSchema>, PipelineBundle<int, Device<BufferSchema>>>, int> Factory => this;

        internal bool ReadReturned { get; set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(4096, [new GraphicsStageRequirement(0, 0)]);

        public IStageProcessor<PipelineBundle<Device<BufferSchema>, PipelineBundle<int, Device<BufferSchema>>>, int> Create(StageBuildContext context) => new Processor(this);

        private sealed class Processor(BundleReadStage stage) : IStageProcessor<PipelineBundle<Device<BufferSchema>, PipelineBundle<int, Device<BufferSchema>>>, int>
        {
            public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<PipelineBundle<Device<BufferSchema>, PipelineBundle<int, Device<BufferSchema>>>> a, CancellationToken cancellationToken)
            {
                GraphicsStageCapabilities capability = Assert.IsType<GraphicsStageCapabilities>(context.Capabilities);
                StageBackendScope scope = context.Backend(capability);
                _ = scope.Read(a);
                stage.ReadReturned = true;
                return ValueTask.FromResult(scope.Publish(1));
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class ForeignPublishStage(Device<BufferSchema> foreign) : IStage<Device<BufferSchema>, Device<BufferSchema>>, IStageFactory<Device<BufferSchema>, Device<BufferSchema>>
    {
        public IStageFactory<Device<BufferSchema>, Device<BufferSchema>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(4096, [new GraphicsStageRequirement(0, 0)]) { OutputMaxElements = 4 };

        public IStageProcessor<Device<BufferSchema>, Device<BufferSchema>> Create(StageBuildContext context) => new Processor(foreign);

        private sealed class Processor(Device<BufferSchema> output) : IStageProcessor<Device<BufferSchema>, Device<BufferSchema>>
        {
            public ValueTask<ValueLease<Device<BufferSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<BufferSchema>> a, CancellationToken cancellationToken)
            {
                GraphicsStageCapabilities capability = Assert.IsType<GraphicsStageCapabilities>(context.Capabilities);
                StageBackendScope scope = context.Backend(capability);
                _ = scope.Read(a);
                return ValueTask.FromResult(scope.Publish(output));
            }

            public void Dispose()
            {
            }
        }
    }
}
