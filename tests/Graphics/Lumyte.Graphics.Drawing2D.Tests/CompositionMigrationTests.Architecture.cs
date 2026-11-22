using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks that construction configuration is not its processing state.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>Checks all eleven generated passive components compile into original sealed handlers.</summary>
    [Fact]
    public void StandardDeclarationsArePassiveAndDistinctFromProcessingTypes()
    {
        Definitions.Renderer declaration = StandardDefinition();
        IReadOnlyList<CommandRouteDescription2D> metadata = declaration.DescribeDataflow();
        Assert.Equal(11, metadata.Count);
        Assert.False(typeof(IDisposable).IsAssignableFrom(declaration.GetType()));
        Assert.Null(declaration.GetType().GetMethod("BuildRenderer"));
        foreach (Type type in typeof(DrawingCompose).Assembly.GetExportedTypes().Where(type => typeof(PipelineDeclaration2D).IsAssignableFrom(type)))
        {
            Assert.False(typeof(IDisposable).IsAssignableFrom(type));
            Assert.DoesNotContain(type.GetMethods(), method => method.Name is "PrepareAsync" or "Record");
            Assert.DoesNotContain(type.GetInterfaces(), contract => contract.Name.EndsWith("Renderer2D", StringComparison.Ordinal));
        }

        Assert.All(metadata, route => Assert.True(route.ConcreteBackendType.IsSealed));
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = declaration.CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        Assert.Same(device, renderer.Device);
    }

    /// <summary>Checks recursive exported signatures cannot reintroduce the removed binding abstraction.</summary>
    [Fact]
    public void ExportedSignaturesHaveNoBindingTypes()
    {
        var visited = new HashSet<Type>();
        void Check(Type type)
        {
            if (!visited.Add(type))
            {
                return;
            }

            Assert.DoesNotContain("BackendBinding", type.Name, StringComparison.Ordinal);
            Assert.False(type.Name.StartsWith("FirstSupport", StringComparison.Ordinal));
            Assert.DoesNotContain("Candidate2D", type.Name, StringComparison.Ordinal);
            if (type.HasElementType)
            {
                Check(type.GetElementType()!);
            }

            foreach (Type argument in type.GetGenericArguments())
            {
                Check(argument);
            }
        }

        foreach (Type type in typeof(DrawingCompose).Assembly.GetExportedTypes())
        {
            Check(type);
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.False(method.Name.StartsWith("FirstSupport", StringComparison.Ordinal));
                Check(method.ReturnType);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Check(parameter.ParameterType);
                }
            }

            foreach (ConstructorInfo constructor in type.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    Check(parameter.ParameterType);
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                Check(field.FieldType);
            }
        }
    }

    /// <summary>Checks passive slot application and inspection do not create processors or evaluate predicates.</summary>
    /// <returns>The asynchronous runtime check.</returns>
    [Fact]
    public async Task PassiveChoicesLowerToIndependentProcessorsOnlyAtBuildAsync()
    {
        int creates = 0;
        int predicates = 0;
        var recipe = new AdmissionRectangleRecipe((_, _) => Admit(true), new DispatchRenderers(), () => creates++);
        bool Admit(bool supported)
        {
            predicates++;
            return supported;
        }

        IPipelineDeclaration2D<FillRectCommand2D>[] candidates = [new AdmissionRectangleRecipe((_, _) => Admit(false), new DispatchRenderers()), recipe];
        Lumyte.Composition.CompositionSlotAssignment<Definitions.Renderer> choice = Renderer.FillRect()[candidates];
        candidates[0] = null!;
        Definitions.Renderer declaration = StandardDefinition();
        _ = declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], choice];
        Assert.Equal(0, creates);
        Assert.Equal(2, declaration.DescribeDataflow()[0].ChoiceBackends.Count);
        Assert.Equal(0, predicates);
        Assert.Equal(0, creates);
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = declaration.CreateHost();
        DrawingResources2D resources = Resources(device);
        using IRenderer2D first = host.BuildRenderer(device, resources, null, null);
        Assert.Equal(1, creates);
        Assert.Equal(0, predicates);
        using IRenderer2D second = host.BuildRenderer(device, resources, null, null);
        Assert.Equal(2, creates);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using PreparedDraw2D prepared = await first.PrepareAsync(RectCircleRect(), new() { View = view });
        Assert.Equal(4, predicates);
    }

    /// <summary>Checks a passive root can be adopted by independent device hosts, not own device state itself.</summary>
    [Fact]
    public void OnePassiveConfigurationHasIndependentDeviceHosts()
    {
        Definitions.Renderer declaration = StandardDefinition();
        var firstDevice = new ProfilingDevice();
        var secondDevice = new ProfilingDevice();
        using RendererBuildHost2D first = declaration.CreateHost();
        using RendererBuildHost2D second = declaration.CreateHost();
        using IRenderer2D one = first.BuildRenderer(firstDevice, Resources(firstDevice), null, null);
        using IRenderer2D two = second.BuildRenderer(secondDevice, Resources(secondDevice), null, null);
        Assert.NotSame(one, two);
        Assert.Same(firstDevice, one.Device);
        Assert.Same(secondDevice, two.Device);
    }

    /// <summary>Checks service factories cannot return owned dependencies already adopted by another build.</summary>
    [Fact]
    public void ReusedOwnedServiceIsRejectedWithoutDisposingLiveFirstBuild()
    {
        var instance = new SharedBackend();
        var recipe = new SharedDeclaration<SharedBackend>("not-fresh", _ => instance);
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = DefineShared(recipe);
        DrawingResources2D resources = Resources(device);
        using IRenderer2D first = host.BuildRenderer(device, resources, null, null);
        Assert.Throws<InvalidOperationException>(() => host.BuildRenderer(device, resources, null, null));
        using RendererBuildHost2D otherHost = DefineShared(recipe);
        Assert.Throws<InvalidOperationException>(() => otherHost.BuildRenderer(device, resources, null, null));
        Assert.Equal(0, instance.Disposals);
        first.Dispose();
        Assert.Equal(1, instance.Disposals);
    }

    /// <summary>Checks a disposed runtime host rejects building while its passive declaration remains usable elsewhere.</summary>
    [Fact]
    public void DisposedHostDoesNotDisposePassiveConfiguration()
    {
        Definitions.Renderer declaration = StandardDefinition();
        RendererBuildHost2D closed = declaration.CreateHost();
        closed.Dispose();
        var device = new ProfilingDevice();
        Assert.Throws<ObjectDisposedException>(() => closed.BuildRenderer(device, Resources(device), null, null));
        using RendererBuildHost2D fresh = declaration.CreateHost();
        using IRenderer2D renderer = fresh.BuildRenderer(device, Resources(device), null, null);
        Assert.Equal(11, declaration.DescribeDataflow().Count);
    }

    /// <summary>Checks failed construction does not consume a caller-owned processor reservation.</summary>
    [Fact]
    public void FailedBuildReleasesCallerProcessorReservationForAnotherHost()
    {
        var device = new ProfilingDevice();
        var caller = new SharedBackend();
        var recipe = new SharedDeclaration<SharedBackend>("caller", _ => caller);
        var failure = new SharedDeclaration<SharedBackend>("failure", _ => throw new InvalidOperationException("creation failed"));
        using RendererBuildHost2D failed = DefineShared(recipe, failure);
        Assert.Throws<InvalidOperationException>(() => failed.BuildRenderer(device, Resources(device), caller, null));
        Assert.Equal(0, caller.Disposals);
        using RendererBuildHost2D retry = DefineShared(recipe);
        using IRenderer2D renderer = retry.BuildRenderer(device, Resources(device), caller, null);
        renderer.Dispose();
        Assert.Equal(1, caller.Disposals);
    }

    /// <summary>Checks invalid returned runtime types still have a reachable host cleanup retry.</summary>
    [Fact]
    public void InvalidCreatedElementCleanupFailureRemainsOwnedByRuntimeHost()
    {
        FaultyElement? element = null;
        var recipe = new BrokenRectangleRecipe(value => element = value);
        Definitions.Renderer declaration = StandardDefinition();
        _ = declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[recipe]];
        using RendererBuildHost2D host = declaration.CreateHost();
        var device = new ProfilingDevice();
        Assert.Throws<AggregateException>(() => host.BuildRenderer(device, Resources(device), null, null));
        Assert.NotNull(element);
        Assert.Equal(1, element.Attempts);
        host.Dispose();
        Assert.Equal(2, element.Attempts);
        host.Dispose();
        Assert.Equal(2, element.Attempts);
    }

    /// <summary>Checks the staged compiler environment cannot resolve new owners after construction.</summary>
    /// <param name="failed">Whether construction throws after capturing the context.</param>
    /// <param name="disposed">Whether the successful renderer has already been disposed.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CapturedConstructionContextCannotCreateLateProcessors(bool failed, bool disposed)
    {
        PipelineBuildContext2D? captured = null;
        int captures = 0;
        void Capture(PipelineBuildContext2D context)
        {
            captures++;
            captured = context;
            if (failed)
            {
                throw new InvalidOperationException("creation failed");
            }
        }

        var recipe = new ContextRectangleRecipe(Capture);
        Definitions.Renderer declaration = StandardDefinition();
        _ = declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[recipe]];
        using RendererBuildHost2D host = declaration.CreateHost();
        var device = new ProfilingDevice();
        using IRenderer2D? renderer = failed ? null : host.BuildRenderer(device, Resources(device), null, null);
        if (failed)
        {
            Assert.Throws<InvalidOperationException>(() => host.BuildRenderer(device, Resources(device), null, null));
        }

        if (disposed)
        {
            renderer!.Dispose();
        }

        Assert.NotNull(captured);
        Assert.Throws<InvalidOperationException>(() => captured.GetProcessor<IFillRectRenderer2D>(recipe));
        Assert.Throws<InvalidOperationException>(() => captured.GetChild<IFillRectRenderer2D>(0));
        Assert.Throws<InvalidOperationException>(() => captured.GetSharedService(new OccurrenceServiceRequest(1, () => { })));
        Assert.Equal(1, captures);
    }

    /// <summary>Checks caller ownership remains reserved until all unpublished dependent cleanup succeeds.</summary>
    [Fact]
    public void FailedDependentCleanupBlocksCallerReadoptionUntilRetryCompletes()
    {
        var device = new ProfilingDevice();
        var caller = new UnretainedManager();
        var dependent = new UnretainedManager { FailDisposeOnce = true };
        var key = new SharedDeclaration<UnretainedManager>("caller", _ => caller);
        var acquired = new SharedDeclaration<UnretainedManager>("dependent", _ => dependent);
        var failure = new SharedDeclaration<UnretainedManager>("failure", _ => throw new InvalidOperationException("later creation failed"));
        using RendererBuildHost2D failed = DefineRaw(key, failure, acquired);
        Assert.Throws<AggregateException>(() => failed.BuildRenderer(device, Resources(device), caller, null));
        using RendererBuildHost2D other = DefineRaw(key);
        DrawingResources2D resources = Resources(device);
        Assert.Throws<InvalidOperationException>(() => other.BuildRenderer(device, resources, caller, null));
        Assert.Equal(0, caller.Disposals);
        Assert.Equal(0, dependent.Disposals);
        failed.Dispose();
        Assert.Equal(1, dependent.Disposals);
        using IRenderer2D renderer = other.BuildRenderer(device, resources, caller, null);
        renderer.Dispose();
        Assert.Equal(1, caller.Disposals);
    }

    /// <summary>Checks diagnostic labels are non-unique while an explicitly equal semantic request shares its service.</summary>
    /// <param name="rootConflict">Whether the conflict joins a choice child with another root slot.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagnosticNamesDoNotMergeOrRejectPlacements(bool rootConflict)
    {
        int creates = 0;
        SharedBackend Create()
        {
            creates++;
            return new SharedBackend();
        }

        var first = new SharedDeclaration<SharedBackend>("duplicate", _ => Create());
        var second = new SharedDeclaration<SharedBackend>("duplicate", _ => Create());
        Definitions.Renderer declaration = StandardDefinition();
        _ = declaration[
            Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()],
            Renderer.FillRect()[rootConflict ? new IPipelineDeclaration2D<FillRectCommand2D>[] { second } : new IPipelineDeclaration2D<FillRectCommand2D>[] { first, second }]];
        if (rootConflict)
        {
            _ = declaration[Renderer.FillCircle()[Array.Empty<IPipelineDeclaration2D<FillCircleCommand2D>>()], Renderer.FillCircle()[first]];
        }

        Assert.Equal(11, declaration.DescribeDataflow().Count);
        Assert.Equal(0, creates);
        using RendererBuildHost2D host = declaration.CreateHost();
        var device = new ProfilingDevice();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        Assert.Equal(1, creates);
    }

    private static Definitions.Renderer StandardDefinition() => Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionLimits(maxFramesInFlight: 3, maxCachedAssets: 512, maxCachedBytes: 64 * 1024 * 1024))[
        Renderer.FillRect()[StandardFillRect()],
        Renderer.FillRoundedRect()[StandardFillRoundedRect()],
        Renderer.FillCircle()[StandardFillCircle()],
        Renderer.DrawLine()[StandardDrawLine()],
        Renderer.FillPath()[StandardFillPath()],
        Renderer.StrokePath()[StandardStrokePath()],
        Renderer.DrawImage()[StandardDrawImage()],
        Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
        Renderer.DrawDistanceField()[StandardDrawDistanceField()],
        Renderer.DrawMesh()[StandardDrawMesh()],
        Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]];

    private sealed class ContextRectangleRecipe(Action<PipelineBuildContext2D> capture) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(ServiceProcessor<SharedBackend>);

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            capture(context);
            return new ServiceProcessor<SharedBackend>(context.GetSharedService(new TestServiceRequest<SharedBackend>("context-test", _ => new SharedBackend())));
        }
    }

    private sealed class BrokenRectangleRecipe(Action<FaultyElement> created) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(IFillRectRenderer2D);

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            var result = new FaultyElement();
            created(result);
            return result;
        }
    }

    private sealed class FaultyElement : PipelineElement2D, IDisposable
    {
        internal int Attempts { get; private set; }

        public void Dispose()
        {
            Attempts++;
            if (Attempts == 1)
            {
                throw new InvalidOperationException("retry cleanup");
            }
        }
    }

    private sealed class RectangleRecipe(int algorithm, Action<RectangleProcessor> onCreated) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(RectangleProcessor);

        internal int Algorithm => algorithm;

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            var processor = new RectangleProcessor(this);
            onCreated(processor);
            return processor;
        }
    }

    private sealed class RectangleProcessor(RectangleRecipe configuration) : PipelineElement2D, IFillRectRenderer2D
    {
        private readonly IFillRectRenderer2D _standard = new DefaultFillRectRenderer2D();

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            Assert.Equal(7, configuration.Algorithm);
            return _standard.PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class AdmissionRectangleRecipe(Func<RenderPreparationContext2D, ReadOnlyMemory<FillRectCommand2D>, bool> supports, IFillRectRenderer2D backend, Action? created = null) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(AdmissionRectangleProcessor);

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            created?.Invoke();
            return new AdmissionRectangleProcessor(supports, backend);
        }
    }

    private sealed class AdmissionRectangleProcessor(Func<RenderPreparationContext2D, ReadOnlyMemory<FillRectCommand2D>, bool> supports, IFillRectRenderer2D backend) : PipelineElement2D, IFillRectRenderer2D, IPipelineAdmission2D<FillRectCommand2D>
    {
        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands) => supports(context, commands);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default) => backend.PrepareAsync(context, commands, cancellationToken);
    }
}
