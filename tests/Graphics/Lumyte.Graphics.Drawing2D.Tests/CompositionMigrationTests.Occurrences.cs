using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks static placement identity independently of semantic internal sharing.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>Checks duplicate passive values create independent nodes, while equal semantic requests share one used manager.</summary>
    /// <param name="sameDeclaration">Whether the candidate value itself is reused.</param>
    /// <returns>The asynchronous runtime check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacementNodesAreFreshButSemanticManagerIsSharedAsync(bool sameDeclaration)
    {
        var created = new List<OccurrenceNode>();
        int services = 0;
        var first = new OccurrenceRecipe(7, () => services++, created.Add);
        OccurrenceRecipe second = sameDeclaration ? first : new OccurrenceRecipe(7, () => services++, created.Add);
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[first, second]];
        Assert.Equal(11, definition.DescribeDataflow().Count);
        Assert.Equal(0, services);
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        Assert.Equal(2, created.Count);
        Assert.NotSame(created[0], created[1]);
        Assert.Same(created[0].Manager, created[1].Manager);
        Assert.Equal(1, services);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        PreparedDraw2D one = await renderer.PrepareAsync(RectCircleRect(), new() { View = view });
        PreparedDraw2D two = await renderer.PrepareAsync(RectCircleRect(), new() { View = view });
        Assert.Equal(4, created[0].Prepares);
        Assert.Equal(0, created[1].Prepares);
        Assert.Equal(4, created[0].Manager.Prepares);
        renderer.Dispose();
        Assert.Equal(0, created[0].Disposals);
        using var commands = new ProfilingCommands();
        one.Record(commands);
        one.Dispose();
        Assert.Equal(0, created[0].Manager.Disposals);
        two.Record(commands);
        two.Dispose();
        Assert.Equal(1, created[0].Manager.Disposals);
        Assert.Equal(4, created[0].Manager.Records);
        Assert.All(created, node => Assert.Equal(1, node.Disposals));
    }

    /// <summary>Checks service identity is options- and build-local, never type-only.</summary>
    [Fact]
    public void SemanticOptionsAndBuildsDoNotShareServices()
    {
        var created = new List<OccurrenceNode>();
        int services = 0;
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[new OccurrenceRecipe(1, () => services++, created.Add), new OccurrenceRecipe(2, () => services++, created.Add)]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        DrawingResources2D resources = Resources(device);
        using IRenderer2D first = host.BuildRenderer(device, resources, null, null);
        using IRenderer2D second = host.BuildRenderer(device, resources, null, null);
        var otherDevice = new ProfilingDevice();
        using RendererBuildHost2D otherHost = definition.CreateHost();
        using IRenderer2D third = otherHost.BuildRenderer(otherDevice, Resources(otherDevice), null, null);
        Assert.Equal(6, services);
        Assert.Equal(6, created.Select(node => node.Manager).Distinct().Count());
    }

    /// <summary>Checks repeated direct-child positions resolve separately but repeated lookup is stable.</summary>
    [Fact]
    public void FrozenChildPositionsAreCachedIndividuallyAndDoNotRetarget()
    {
        int enumerations = 0;
        var nodes = new List<OccurrenceNode>();
        var child = new OccurrenceRecipe(1, () => { }, nodes.Add);
        var parent = new ChildOccurrenceRecipe(child, () => enumerations++);
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[parent]];
        CommandRouteDescription2D description = definition.DescribeDataflow()[0];
        Assert.Equal(new[] { "FillRect/0", "FillRect/0/0", "FillRect/0/1" }, description.OccurrencePaths);
        Assert.Equal(1, enumerations);
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        Assert.Equal(1, enumerations);
        Assert.Equal(2, nodes.Count);
        Assert.NotSame(nodes[0], nodes[1]);
        Assert.Same(nodes[0].Manager, nodes[1].Manager);
        Assert.Throws<InvalidOperationException>(() => parent.Captured!.GetChild<OccurrenceNode>(0));
        Assert.Throws<InvalidOperationException>(() => parent.Captured!.GetSharedService(new OccurrenceServiceRequest(1, () => { })));
    }

    /// <summary>Checks one runtime node cannot be adopted at two placements and foreign state is not disposed.</summary>
    [Fact]
    public void ReturningSameProcessorAtDifferentPositionsIsRejectedBeforeOwnershipTransfer()
    {
        var node = new OccurrenceNode(new OccurrenceManager());
        var recipe = new ExistingOccurrenceRecipe(node);
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[recipe]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D firstHost = definition.CreateHost();
        using IRenderer2D first = firstHost.BuildRenderer(device, Resources(device), null, null);
        Definitions.Renderer secondDefinition = StandardDefinition();
        _ = secondDefinition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[recipe, recipe]];
        using RendererBuildHost2D secondHost = secondDefinition.CreateHost();
        Assert.Throws<InvalidOperationException>(() => secondHost.BuildRenderer(device, Resources(device), null, null));
        Assert.Equal(0, node.Disposals);
        first.Dispose();
        Assert.Equal(1, node.Disposals);
    }

    /// <summary>Checks same-key incompatible versions, factory identities and recursion reject deterministically.</summary>
    /// <param name="mode">The incompatible dependency scenario.</param>
    [Theory]
    [InlineData("version")]
    [InlineData("factory")]
    [InlineData("cycle")]
    public void IncompatibleOrRecursiveSemanticRequestsRejectAndRollback(string mode)
    {
        OccurrenceManager? manager = null;
        var recipe = new CallbackOccurrenceRecipe(context =>
        {
            if (mode == "cycle")
            {
                context.GetSharedService(new RecursiveServiceRequest());
            }
            else
            {
                manager = context.GetSharedService(new VersionedServiceRequest(1));
                if (mode == "version")
                {
                    context.GetSharedService(new VersionedServiceRequest(2));
                }
                else
                {
                    context.GetSharedService(new DifferentServiceRequest());
                }
            }

            throw new InvalidOperationException("Invalid semantic request unexpectedly succeeded.");
        });
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[recipe]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        Assert.Throws<ArgumentException>(() => host.BuildRenderer(device, Resources(device), null, null));
        if (manager is not null)
        {
            Assert.Equal(1, manager.Disposals);
        }
    }

    /// <summary>Checks failed nested factories retain created dependencies for scope cleanup and retry.</summary>
    [Fact]
    public void NestedServiceDependenciesAreCleanedOnFailedFactoryAndCanRetry()
    {
        OccurrenceManager? dependency = null;
        int attempts = 0;
        var recipe = new CallbackOccurrenceRecipe(context =>
        {
            var request = new NestedServiceRequest(() => ++attempts == 1, value => dependency = value);
            return new OccurrenceNode(context.GetSharedService(request));
        });
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[recipe]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        DrawingResources2D resources = Resources(device);
        Assert.Throws<InvalidOperationException>(() => host.BuildRenderer(device, resources, null, null));
        Assert.NotNull(dependency);
        Assert.Equal(1, dependency.Disposals);
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        Assert.Equal(2, attempts);
        Assert.Equal(0, dependency.Disposals);
        renderer.Dispose();
        Assert.Equal(1, dependency.Disposals);
    }

    /// <summary>Checks invalid processor results cannot transfer another live build's shared service.</summary>
    [Fact]
    public void InvalidProcessorReturningForeignServiceDoesNotDisposeItsOwner()
    {
        OccurrenceNode? node = null;
        Definitions.Renderer firstDefinition = StandardDefinition();
        _ = firstDefinition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[new OccurrenceRecipe(1, () => { }, value => node = value)]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D firstHost = firstDefinition.CreateHost();
        using IRenderer2D first = firstHost.BuildRenderer(device, Resources(device), null, null);
        Assert.NotNull(node);
        Definitions.Renderer invalid = StandardDefinition();
        _ = invalid[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[new InvalidServiceProcessorRecipe(node.Manager)]];
        using RendererBuildHost2D invalidHost = invalid.CreateHost();
        Assert.Throws<InvalidOperationException>(() => invalidHost.BuildRenderer(device, Resources(device), null, null));
        Assert.Equal(0, node.Manager.Disposals);
        first.Dispose();
        Assert.Equal(1, node.Manager.Disposals);
    }

    /// <summary>Checks the second placement in the same build cannot adopt the first placement's processor.</summary>
    [Fact]
    public void SameBuildProcessorAliasFailsAtSecondPlacementAndRollsBackOnce()
    {
        var node = new OccurrenceNode(new OccurrenceManager());
        var recipe = new ExistingOccurrenceRecipe(node);
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[recipe, recipe]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        Assert.Throws<InvalidOperationException>(() => host.BuildRenderer(device, Resources(device), null, null));
        Assert.Equal(1, node.Disposals);
        host.Dispose();
        Assert.Equal(1, node.Disposals);
    }

    /// <summary>Checks an escaped service callback context closes independently of its still-active processor callback.</summary>
    /// <param name="failed">Whether the service factory throws and the processor catches it.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServiceCallbackContextClosesAtItsOwnReturnOrFailure(bool failed)
    {
        PipelineBuildContext2D? captured = null;
        var recipe = new CallbackOccurrenceRecipe(context =>
        {
            var request = new CapturingServiceRequest(failed, value => captured = value);
            if (failed)
            {
                Assert.Throws<InvalidOperationException>(() => context.GetSharedService(request));
            }
            else
            {
                context.GetSharedService(request);
            }

            Assert.NotNull(captured);
            Assert.Throws<InvalidOperationException>(() => captured.GetSharedService(new OccurrenceServiceRequest(2, () => { })));
            Assert.Throws<InvalidOperationException>(() => captured.GetChild<OccurrenceNode>(0));
            return new OccurrenceNode(context.GetSharedService(new OccurrenceServiceRequest(3, () => { })));
        });
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[recipe]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
    }

    private sealed class InvalidServiceProcessorRecipe(OccurrenceManager manager) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(IFillRectRenderer2D);

        protected override object CreateElement(PipelineBuildContext2D context) => manager;
    }

    private sealed class CapturingServiceRequest(bool failed, Action<PipelineBuildContext2D> capture) : PipelineServiceRequest2D<OccurrenceManager, int>("capturing", 1, 1)
    {
        protected override OccurrenceManager CreateService(PipelineBuildContext2D context)
        {
            capture(context);
            if (failed)
            {
                throw new InvalidOperationException("Service creation failed.");
            }

            return new OccurrenceManager();
        }
    }

    private sealed class CallbackOccurrenceRecipe(Func<PipelineBuildContext2D, OccurrenceNode> create) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(OccurrenceNode);

        protected override object CreateElement(PipelineBuildContext2D context) => create(context);
    }

    private sealed class VersionedServiceRequest(int version) : PipelineServiceRequest2D<OccurrenceManager, int>("versioned", 1, version)
    {
        protected override OccurrenceManager CreateService(PipelineBuildContext2D context) => new();
    }

    private sealed class DifferentServiceRequest() : PipelineServiceRequest2D<OccurrenceManager, int>("versioned", 1, 1)
    {
        protected override OccurrenceManager CreateService(PipelineBuildContext2D context) => new();
    }

    private sealed class RecursiveServiceRequest() : PipelineServiceRequest2D<OccurrenceManager, int>("recursive", 1, 1)
    {
        protected override OccurrenceManager CreateService(PipelineBuildContext2D context) => context.GetSharedService(new RecursiveServiceRequest());
    }

    private sealed class NestedServiceRequest(Func<bool> fail, Action<OccurrenceManager> captured) : PipelineServiceRequest2D<OccurrenceManager, int>("outer", 1, 1)
    {
        protected override OccurrenceManager CreateService(PipelineBuildContext2D context)
        {
            OccurrenceManager dependency = context.GetSharedService(new OccurrenceServiceRequest(1, () => { }));
            captured(dependency);
            if (fail())
            {
                throw new InvalidOperationException("Nested factory failed.");
            }

            return new OccurrenceManager();
        }
    }

    private sealed class ExistingOccurrenceRecipe(OccurrenceNode node) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(OccurrenceNode);

        protected override object CreateElement(PipelineBuildContext2D context) => node;
    }

    private sealed class ChildOccurrenceRecipe(PipelineDeclaration2D child, Action enumerated) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(OccurrenceNode);

        internal PipelineBuildContext2D? Captured { get; private set; }

        protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations()
        {
            enumerated();
            return [child, child];
        }

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            Captured = context;
            OccurrenceNode first = context.GetChild<OccurrenceNode>(0);
            Assert.Same(first, context.GetChild<OccurrenceNode>(0));
            Assert.NotSame(first, context.GetChild<OccurrenceNode>(1));
            Assert.Throws<ArgumentException>(() => context.GetProcessor<OccurrenceNode>(child));
            Assert.Throws<ArgumentException>(() => context.GetProcessor<OccurrenceNode>(this));
            return new OccurrenceNode(first.Manager);
        }
    }

    private sealed class OccurrenceRecipe(int options, Action serviceCreated, Action<OccurrenceNode> created) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(OccurrenceNode);

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            var result = new OccurrenceNode(context.GetSharedService(new OccurrenceServiceRequest(options, serviceCreated)));
            created(result);
            return result;
        }
    }

    private sealed class OccurrenceServiceRequest(int options, Action created) : PipelineServiceRequest2D<OccurrenceManager, int>("immutable-algorithm", options, 1)
    {
        protected override OccurrenceManager CreateService(PipelineBuildContext2D context)
        {
            created();
            return new OccurrenceManager();
        }
    }

    private sealed class OccurrenceManager : PipelineSharedService2D
    {
        private readonly DispatchRenderers _backend = new();

        internal int Prepares { get; private set; }

        internal int Disposals { get; private set; }

        internal int Records { get; private set; }

        public override void Dispose() => Disposals++;

        internal async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
        {
            Assert.Equal(0, Disposals);
            Prepares++;
            return new OccurrenceBatch(await ((IFillRectRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken), this);
        }

        private sealed class OccurrenceBatch(IPreparedCommandBatch2D batch, OccurrenceManager manager) : IPreparedCommandBatch2D
        {
            public void DeclareResources(IDrawingResourceDeclarations2D resources) => batch.DeclareResources(resources);

            public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
            {
                Assert.Equal(0, manager.Disposals);
                manager.Records++;
                batch.Record(commands, target);
            }

            public void Dispose() => batch.Dispose();
        }
    }

    private sealed class OccurrenceNode(OccurrenceManager manager) : PipelineElement2D, IFillRectRenderer2D, IDisposable
    {
        internal OccurrenceManager Manager => manager;

        internal int Prepares { get; private set; }

        internal int Disposals { get; private set; }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            Prepares++;
            return manager.PrepareAsync(context, commands, cancellationToken);
        }

        public void Dispose()
        {
            Assert.Equal(0, manager.Disposals);
            Disposals++;
        }
    }
}
