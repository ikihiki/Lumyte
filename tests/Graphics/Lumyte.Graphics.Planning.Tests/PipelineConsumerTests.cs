using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Proves typed Core factories flatten into individually visible common GPU commands.</summary>
public sealed class PipelineConsumerTests
{
    /// <summary>Two template invocations share one frame and do not recursively prepare or submit a GPU plan.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task TypedInvocationsDeclareTwoCopyPassesAndExpireTheirCapabilitiesAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        var stage = new CopyStage();
        var template = new PipelineGraph<Device<IntSchema>>();
        PipelineDefinition<Device<IntSchema>, Device<IntSchema>> definition = template.Complete(template.Input.Then(stage));
        var graph = new PipelineGraph<Device<IntSchema>>();
        Port<Device<IntSchema>> first = graph.Invoke(definition, graph.Input);
        PipelineDefinition<Device<IntSchema>, Device<IntSchema>> final = graph.Complete(graph.Invoke(definition, first));
        var visitor = new FactoryVisitor();
        final.Visit(visitor);
        Assert.Equal(2, visitor.Factories.Count);
        Assert.All(visitor.Factories, factory => Assert.Same(stage, factory));
        using var build = new PipelineBuildHost(host.Runtime, new(16, 65536, 1), new GraphicsPipelineBackend(frame.Value));
        using IPipeline<Device<IntSchema>, Device<IntSchema>> pipeline = build.Build(final);
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        Device<IntSchema> input = frame.Plan.BufferValue<IntSchema, int>(source, new(4, new Dictionary<string, object>()));
        using PipelineResult<Device<IntSchema>> result = await pipeline.PrepareAsync(input);
        Assert.Empty(native.Events);
        Assert.Throws<InvalidOperationException>(() => stage.LastScope!.AddPass("escaped"));
        Assert.Throws<InvalidOperationException>(() => stage.LastPass!.KeepAlive());
        frame.Plan.Export(frame.Plan.Resource(result.Value));
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        Assert.Equal(2, compiled.Passes.Count);
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.ExecuteAsync();
        Assert.Equal(2, native.Events.Count(e => e.StartsWith("copy:", StringComparison.Ordinal)));
        Assert.Equal(1, native.Events.Count(e => e == "submit"));
    }

    private sealed class IntSchema : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(int), sizeof(int), new Dictionary<string, object>());
    }

    private sealed class CopyStage : IStage<Device<IntSchema>, Device<IntSchema>>, IStageFactory<Device<IntSchema>, Device<IntSchema>>
    {
        public IStageFactory<Device<IntSchema>, Device<IntSchema>> Factory => this;

        internal GraphicsDeclarationScope? LastScope { get; set; }

        internal GraphicsPassScope? LastPass { get; set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(4096, [new GraphicsStageRequirement(1, 1)]) { OutputMaxElements = 4 };

        public IStageProcessor<Device<IntSchema>, Device<IntSchema>> Create(StageBuildContext context) => new Processor(this);

        private sealed class Processor(CopyStage stage) : IStageProcessor<Device<IntSchema>, Device<IntSchema>>
        {
            public ValueTask<ValueLease<Device<IntSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<IntSchema>> a, CancellationToken cancellationToken)
            {
                GraphicsStageCapabilities graphics = Assert.IsType<GraphicsStageCapabilities>(context.Capabilities);
                PlanBuffer<int> source = Assert.IsType<PlanBuffer<int>>(graphics.Read(a));
                GraphicsDeclarationScope scope = graphics.Plan;
                stage.LastScope = scope;
                PlanBuffer<int> undefined = scope.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
                Assert.Throws<InvalidOperationException>(() => scope.CreateBuffer<int>(new() { Count = 1, Usage = BufferUsage.CopyDestination }));
                GraphicsPassScope pass = scope.AddPass("typed-copy");
                Assert.Throws<InvalidOperationException>(() => scope.AddPass("over-quota"));
                stage.LastPass = pass;
                pass.Read(source, source.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
                PlanBuffer<int> destination = pass.Write(undefined, undefined.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
                pass.Copy(source, source.Range, destination, destination.Range);
                return ValueTask.FromResult(graphics.Publish<IntSchema, int>(destination, new(4, new Dictionary<string, object>())));
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class FactoryVisitor : IPipelineDefinitionVisitor
    {
        internal List<object> Factories { get; } = [];

        public void Stage<TA, TO>(IStageFactory<TA, TO> factory, PipelineEdge<TA> inputs, PipelineEdge<TO> output) => Factories.Add(factory);

        public void Stage<TA, TB, TO>(IStageFactory<TA, TB, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B) inputs, PipelineEdge<TO> output) => Factories.Add(factory);

        public void Stage<TA, TB, TC, TO>(IStageFactory<TA, TB, TC, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C) inputs, PipelineEdge<TO> output) => Factories.Add(factory);

        public void Stage<TA, TB, TC, TD, TO>(IStageFactory<TA, TB, TC, TD, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D) inputs, PipelineEdge<TO> output) => Factories.Add(factory);

        public void Stage<TA, TB, TC, TD, TE, TO>(IStageFactory<TA, TB, TC, TD, TE, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D, PipelineEdge<TE> E) inputs, PipelineEdge<TO> output) => Factories.Add(factory);

        public void Stage<TA, TB, TC, TD, TE, TF, TO>(IStageFactory<TA, TB, TC, TD, TE, TF, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D, PipelineEdge<TE> E, PipelineEdge<TF> F) inputs, PipelineEdge<TO> output) => Factories.Add(factory);
    }
}
