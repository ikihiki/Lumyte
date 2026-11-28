using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Public-only captured-factory compiler and same-graph composition tests.</summary>
public sealed class DefinitionCompilerTests
{
    /// <summary>Parameters carried by private generic factory instances.</summary>
    private interface IParameters
    {
        /// <summary>Gets the original parameter object.</summary>
        object Parameters { get; }

        /// <summary>Gets the processor creation count.</summary>
        int Creates { get; }
    }

    /// <summary>All six arities expose real generic factories and parameter instances without reflection.</summary>
    [Fact]
    public void VisitorCapturesAllAritiesAndPrivateGenericParameters()
    {
        var graph = new PipelineGraph<int>();
        var parameters = new Parameters(7);
        var factories = new IStageFactory[6];
        var stage1 = new Stage1<Parameters>(parameters);
        factories[0] = stage1;
        Port<int> edge1 = graph.Stage(stage1, graph.Input);
        var stage2 = new Stage2<Parameters>(parameters);
        factories[1] = stage2;
        Port<int> edge2 = graph.Stage(stage2, edge1, edge1);
        var stage3 = new Stage3<Parameters>(parameters);
        factories[2] = stage3;
        Port<int> edge3 = graph.Stage(stage3, edge2, edge2, edge2);
        var stage4 = new Stage4<Parameters>(parameters);
        factories[3] = stage4;
        Port<int> edge4 = graph.Stage(stage4, edge3, edge3, edge3, edge3);
        var stage5 = new Stage5<Parameters>(parameters);
        factories[4] = stage5;
        Port<int> edge5 = graph.Stage(stage5, edge4, edge4, edge4, edge4, edge4);
        var stage6 = new Stage6<Parameters>(parameters);
        factories[5] = stage6;
        Port<int> edge6 = graph.Stage(stage6, edge5, edge5, edge5, edge5, edge5, edge5);
        PipelineDefinition<int, int> definition = graph.Complete(edge6);
        var visitor = new Visitor((factory, inputs, output) =>
        {
            Assert.Same(factories[output - 1], factory);
            Assert.Same(parameters, Assert.IsAssignableFrom<IParameters>(factory).Parameters);
            Assert.Equal(output, inputs.Length);
            Assert.All(inputs, index => Assert.Equal(output - 1, index));
        });
        definition.Visit(visitor);
        Assert.Equal(6, definition.OutputEdge.Index);
        Assert.All(factories, factory => Assert.Equal(0, Assert.IsAssignableFrom<IParameters>(factory).Creates));
    }

    /// <summary>Repeated template invocations append finite instances and preserve shared producers inside each instance.</summary>
    [Fact]
    public void InvokeFlattensFactoriesAndSharingWithoutNestedRuns()
    {
        var template = new PipelineGraph<int>();
        var stage = new Stage1<Parameters>(new Parameters(2));
        Port<int> shared = template.Stage(stage, template.Input);
        Port<int> output = template.Stage(new Stage2<Parameters>(new Parameters(0)), shared, shared);
        PipelineDefinition<int, int> definition = template.Complete(output);
        var parent = new PipelineGraph<int>();
        Port<int> first = parent.Invoke(definition, parent.Input);
        Port<int> second = parent.Invoke(definition, first);
        PipelineDefinition<int, int> flat = parent.Complete(second);
        var edges = new List<int[]>();
        var factories = new List<IStageFactory>();
        flat.Visit(new Visitor((factory, inputs, index) =>
        {
            factories.Add(factory);
            edges.Add(inputs);
        }));
        Assert.Equal(4, flat.OutputEdge.Index);
        Assert.Equal([0], edges[0]);
        Assert.Equal([1, 1], edges[1]);
        Assert.Equal([2], edges[2]);
        Assert.Equal([3, 3], edges[3]);
        Assert.Same(stage, factories[0]);
        Assert.Same(stage, factories[2]);
        Assert.Equal(0, stage.Creates);
        using var host = new PipelineBuildHost(new ExecutionRuntime([]), new PipelineBudget(3, 4096, 1));
        Assert.Throws<InvalidOperationException>(() => host.Build(flat));
        Assert.Equal(0, stage.Creates);
        Assert.Equal(20, Execute(flat, 5));
        Assert.Equal(2, stage.Creates);
    }

    /// <summary>Foreign ports, frozen parents and mutated schemas cannot be rebound.</summary>
    [Fact]
    public void InvokeValidatesGraphFreezeAndSchemaBeforeAppending()
    {
        var identity = new PipelineGraph<int>();
        PipelineDefinition<int, int> definition = identity.Complete(identity.Input);
        var parent = new PipelineGraph<int>();
        var foreign = new PipelineGraph<int>();
        Assert.Throws<ArgumentException>(() => parent.Invoke(definition, foreign.Input));
        Port<int> rebound = parent.Invoke(definition, parent.Input);
        Assert.Equal(0, parent.Complete(rebound).OutputEdge.Index);
        Assert.Throws<InvalidOperationException>(() => parent.Invoke(definition, parent.Input));

        MutablePlacement.Version = 1;
        var schemaTemplate = new PipelineGraph<MutablePlacement>();
        PipelineDefinition<MutablePlacement, MutablePlacement> schemaDefinition = schemaTemplate.Complete(schemaTemplate.Input);
        MutablePlacement.Version = 2;
        var schemaParent = new PipelineGraph<MutablePlacement>();
        Assert.Throws<InvalidOperationException>(() => schemaParent.Invoke(schemaDefinition, schemaParent.Input));
        Assert.Throws<InvalidOperationException>(() => schemaDefinition.Visit(new Visitor((factory, inputs, output) => { })));
        MutablePlacement.Version = 1;
    }

    /// <summary>Nested opaque schemas are captured, and maliciously unbounded declared shapes fail finitely.</summary>
    [Fact]
    public void RecursiveBundleSchemasAndTraversalAreBounded()
    {
        MutablePlacement.Version = 10;
        var template = new PipelineGraph<PipelineBundle<int, PipelineBundle<MutablePlacement, int>>>();
        PipelineDefinition<PipelineBundle<int, PipelineBundle<MutablePlacement, int>>, PipelineBundle<int, PipelineBundle<MutablePlacement, int>>> definition = template.Complete(template.Input);
        MutablePlacement.Version = 11;
        Assert.Throws<InvalidOperationException>(() => definition.Visit(new Visitor((factory, inputs, output) => { })));
        var parent = new PipelineGraph<PipelineBundle<int, PipelineBundle<MutablePlacement, int>>>();
        Assert.Throws<InvalidOperationException>(() => parent.Invoke(definition, parent.Input));
        MutablePlacement.Version = 10;

        var recursive = new PipelineGraph<RecursiveBundle>();
        Assert.Throws<InvalidOperationException>(() => recursive.Complete(recursive.Input));
        var wide = new PipelineGraph<WideBundle>();
        Assert.Throws<InvalidOperationException>(() => wide.Complete(wide.Input));
    }

    private static int Execute(PipelineDefinition<int, int> definition, int input)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            using var host = new PipelineBuildHost(runtime, new PipelineBudget(32, 4096, 2));
            using IPipeline<int, int> pipeline = host.Build(definition);
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(input).AsTask();
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.GetAwaiter().GetResult();
            return result.Value;
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

    private readonly struct RecursiveBundle : IPipelineBundle
    {
        public void VisitChildren(IPipelineBundleVisitor visitor) => visitor.Child(this);
    }

    private readonly struct WideBundle : IPipelineBundle
    {
        public void VisitChildren(IPipelineBundleVisitor visitor)
        {
            for (int i = 0; i < 65; i++)
            {
                visitor.Child(i);
            }
        }
    }

    private readonly struct MutablePlacement : INonCpuReadablePlacement
    {
        public SchemaDescriptor Descriptor => new(Version, typeof(int), 4, new Dictionary<string, object>());

        public SchemaExtent Extent => new(1, new Dictionary<string, object>());

        public string DiagnosticPlacement => "external";

        internal static int Version { get; set; }
    }

    private sealed record Parameters(int Delta);

    private sealed class Visitor(Action<IStageFactory, int[], int> visit) : IPipelineDefinitionVisitor
    {
        public void Stage<TA, TO>(IStageFactory<TA, TO> factory, PipelineEdge<TA> inputs, PipelineEdge<TO> output)
            => visit(factory, [inputs.Index], output.Index);

        public void Stage<TA, TB, TO>(IStageFactory<TA, TB, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B) inputs, PipelineEdge<TO> output)
            => visit(factory, [inputs.A.Index, inputs.B.Index], output.Index);

        public void Stage<TA, TB, TC, TO>(IStageFactory<TA, TB, TC, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C) inputs, PipelineEdge<TO> output)
            => visit(factory, [inputs.A.Index, inputs.B.Index, inputs.C.Index], output.Index);

        public void Stage<TA, TB, TC, TD, TO>(IStageFactory<TA, TB, TC, TD, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D) inputs, PipelineEdge<TO> output)
            => visit(factory, [inputs.A.Index, inputs.B.Index, inputs.C.Index, inputs.D.Index], output.Index);

        public void Stage<TA, TB, TC, TD, TE, TO>(IStageFactory<TA, TB, TC, TD, TE, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D, PipelineEdge<TE> E) inputs, PipelineEdge<TO> output)
            => visit(factory, [inputs.A.Index, inputs.B.Index, inputs.C.Index, inputs.D.Index, inputs.E.Index], output.Index);

        public void Stage<TA, TB, TC, TD, TE, TF, TO>(IStageFactory<TA, TB, TC, TD, TE, TF, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D, PipelineEdge<TE> E, PipelineEdge<TF> F) inputs, PipelineEdge<TO> output)
            => visit(factory, [inputs.A.Index, inputs.B.Index, inputs.C.Index, inputs.D.Index, inputs.E.Index, inputs.F.Index], output.Index);
    }

    private sealed class Stage1<TParameters>(TParameters parameters) : IStage<int, int>, IStageFactory<int, int>, IStageProcessor<int, int>, IParameters
        where TParameters : class
    {
        public IStageFactory<int, int> Factory => this;

        public object Parameters => parameters;

        public int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []);

        public IStageProcessor<int, int> Create(StageBuildContext context)
        {
            Creates++;
            return new Stage1<TParameters>(parameters);
        }

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a)));

        public void Dispose()
        {
        }
    }

    private sealed class Stage2<TParameters>(TParameters parameters) : IStage<int, int, int>, IStageFactory<int, int, int>, IStageProcessor<int, int, int>, IParameters
        where TParameters : class
    {
        public IStageFactory<int, int, int> Factory => this;

        public object Parameters => parameters;

        public int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []);

        public IStageProcessor<int, int, int> Create(StageBuildContext context)
        {
            Creates++;
            return new Stage2<TParameters>(parameters);
        }

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b)));

        public void Dispose()
        {
        }
    }

    private sealed class Stage3<TParameters>(TParameters parameters) : IStage<int, int, int, int>, IStageFactory<int, int, int, int>, IStageProcessor<int, int, int, int>, IParameters
        where TParameters : class
    {
        public IStageFactory<int, int, int, int> Factory => this;

        public object Parameters => parameters;

        public int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []);

        public IStageProcessor<int, int, int, int> Create(StageBuildContext context)
        {
            Creates++;
            return new Stage3<TParameters>(parameters);
        }

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, ValueLease<int> c, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b) + context.Read(c)));

        public void Dispose()
        {
        }
    }

    private sealed class Stage4<TParameters>(TParameters parameters) : IStage<int, int, int, int, int>, IStageFactory<int, int, int, int, int>, IStageProcessor<int, int, int, int, int>, IParameters
        where TParameters : class
    {
        public IStageFactory<int, int, int, int, int> Factory => this;

        public object Parameters => parameters;

        public int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []);

        public IStageProcessor<int, int, int, int, int> Create(StageBuildContext context)
        {
            Creates++;
            return new Stage4<TParameters>(parameters);
        }

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, ValueLease<int> c, ValueLease<int> d, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b) + context.Read(c) + context.Read(d)));

        public void Dispose()
        {
        }
    }

    private sealed class Stage5<TParameters>(TParameters parameters) : IStage<int, int, int, int, int, int>, IStageFactory<int, int, int, int, int, int>, IStageProcessor<int, int, int, int, int, int>, IParameters
        where TParameters : class
    {
        public IStageFactory<int, int, int, int, int, int> Factory => this;

        public object Parameters => parameters;

        public int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []);

        public IStageProcessor<int, int, int, int, int, int> Create(StageBuildContext context)
        {
            Creates++;
            return new Stage5<TParameters>(parameters);
        }

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, ValueLease<int> c, ValueLease<int> d, ValueLease<int> e, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b) + context.Read(c) + context.Read(d) + context.Read(e)));

        public void Dispose()
        {
        }
    }

    private sealed class Stage6<TParameters>(TParameters parameters) : IStage<int, int, int, int, int, int, int>, IStageFactory<int, int, int, int, int, int, int>, IStageProcessor<int, int, int, int, int, int, int>, IParameters
        where TParameters : class
    {
        public IStageFactory<int, int, int, int, int, int, int> Factory => this;

        public object Parameters => parameters;

        public int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []);

        public IStageProcessor<int, int, int, int, int, int, int> Create(StageBuildContext context)
        {
            Creates++;
            return new Stage6<TParameters>(parameters);
        }

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, ValueLease<int> c, ValueLease<int> d, ValueLease<int> e, ValueLease<int> f, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b) + context.Read(c) + context.Read(d) + context.Read(e) + context.Read(f)));

        public void Dispose()
        {
        }
    }
}
