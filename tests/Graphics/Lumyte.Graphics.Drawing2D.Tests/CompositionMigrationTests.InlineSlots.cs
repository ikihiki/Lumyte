using Lumyte.Composition;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks inline ordered slot admission and assignment snapshots.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>Checks single and multiple admissions, early stopping and non-retry errors through the public slot API.</summary>
    /// <param name="mode">The rejection or failure scenario.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InlineSlotsSelectBeforeAnyPreparationAsync(int mode)
    {
        var probes = new List<int>();
        var backend = new DispatchRenderers
        {
            OnPreparing = _ =>
            {
                if (mode == 4)
                {
                    throw new InvalidOperationException("prepare failure");
                }
            },
        };
        var never = new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unrelated kind prepared") };
        var first = new AdmissionRectangleRecipe(
            (_, _) =>
            {
                probes.Add(0);
                return mode == 3 ? throw new InvalidOperationException("admission failure") : mode is 1 or 4;
            },
            backend);
        var second = new AdmissionRectangleRecipe(
            (_, _) =>
            {
                probes.Add(1);
                return mode != 2;
            },
            backend);
        var third = new AdmissionRectangleRecipe((_, _) => throw new InvalidOperationException("late candidate probed"), backend);
        Definitions.Renderer declaration = StandardDefinition();
        IPipelineDeclaration2D<FillRectCommand2D>[] candidates = mode == 5 ? [first] : mode == 2 ? [first, second] : [first, second, third];
        _ = declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[candidates], Renderer.FillCircle()[Array.Empty<IPipelineDeclaration2D<FillCircleCommand2D>>()], Renderer.FillCircle()[new CircleAdmissionRecipe(never)]];
        using RendererBuildHost2D host = declaration.CreateHost();
        var device = new ProfilingDevice();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 4, 4), new SRgba<float>(1, 0, 0, 1));
        DrawList2D list = canvas.End();
        if (mode is 2 or 5)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => renderer.PrepareAsync(list, new() { View = view }).AsTask());
            Assert.Empty(backend.Commands);
        }
        else if (mode is 3 or 4)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.PrepareAsync(list, new() { View = view }).AsTask());
        }
        else
        {
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view });
            Assert.Single(backend.Commands);
        }

        Assert.Equal(mode is 0 or 2 ? new[] { 0, 1 } : new[] { 0 }, probes);
    }

    /// <summary>Checks capture/apply copies, reusable assignment, atomic malformed lists and explicit clearing.</summary>
    [Fact]
    public void InlineSlotSnapshotsAreReusableAndMalformedListsAreAtomic()
    {
        Definitions.StandardFillRect first = StandardFillRect();
        Definitions.StandardFillRect second = StandardFillRect();
        IPipelineDeclaration2D<FillRectCommand2D>[] input = [first, second];
        CompositionSlotAssignment<Definitions.Renderer> assignment = Renderer.FillRect()[input];
        input[0] = null!;
        Definitions.Renderer one = StandardDefinition();
        Definitions.Renderer two = StandardDefinition();
        foreach (Definitions.Renderer declaration in new[] { one, two })
        {
            _ = declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()]];
            Assert.Throws<ArgumentException>(() => _ = declaration[Renderer.FillRect()[first, null!]]);
            _ = declaration[Renderer.FillRect()[first, first]];
            _ = declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()]];
            assignment.Apply(declaration);
            Assert.Throws<ArgumentException>(() => assignment.Apply(declaration));
            Assert.Equal(new[] { typeof(DefaultFillRectRenderer2D), typeof(DefaultFillRectRenderer2D) }, declaration.DescribeDataflow()[0].ChoiceBackends);
            Assert.Throws<InvalidOperationException>(() => assignment.Apply(declaration));
        }
    }

    /// <summary>Checks nested/root references resolve the same runtime processor once, including nonselected candidates.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task InlineCandidatesShareNestedProcessorsWithOtherKindSlotsAsync()
    {
        int creates = 0;
        var recipe = new SharedDeclaration<SharedBackend>(
            "nested-shared",
            _ =>
            {
                creates++;
                return new SharedBackend();
            });
        var nested = new NestedRectangleRecipe(recipe);
        Definitions.Renderer declaration = StandardDefinition();
        _ = declaration[
            Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()],
            Renderer.FillRect()[nested, recipe],
            Renderer.FillCircle()[Array.Empty<IPipelineDeclaration2D<FillCircleCommand2D>>()],
            Renderer.FillCircle()[recipe]];
        using RendererBuildHost2D host = declaration.CreateHost();
        var device = new ProfilingDevice();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        Assert.Equal(1, creates);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using PreparedDraw2D prepared = await renderer.PrepareAsync(RectCircleRect(), new() { View = view });
        Assert.Equal(3, nested.Instance!.Manager.Calls.Count);
    }

    /// <summary>Checks nested capabilities and cycles are validated before any factory.</summary>
    /// <param name="cycle">Whether the child topology is cyclic rather than wrongly typed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineCandidateNestedTopologyRejectsBeforeCreation(bool cycle)
    {
        var invalid = new InvalidNestedRecipe(cycle);
        Definitions.Renderer declaration = StandardDefinition();
        _ = declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[StandardFillRect(), invalid]];
        Assert.Throws<ArgumentException>(() => declaration.DescribeDataflow());
        Assert.Equal(0, invalid.Creates);
    }

    /// <summary>Checks all eleven typed slots use their runtime admission protocol and stop after acceptance.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task AllInlineSlotsHonorRuntimeAdmissionBeforePreparingAsync()
    {
        var backend = new DispatchRenderers();
        var rejected = new AllKindAdmissionRecipe(false, backend);
        var accepted = new AllKindAdmissionRecipe(true, backend);
        Definitions.Renderer declaration = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D())[
            Renderer.FillRect()[rejected, accepted],
            Renderer.FillRoundedRect()[rejected, accepted],
            Renderer.FillCircle()[rejected, accepted],
            Renderer.DrawLine()[rejected, accepted],
            Renderer.FillPath()[rejected, accepted],
            Renderer.StrokePath()[rejected, accepted],
            Renderer.DrawImage()[rejected, accepted],
            Renderer.DrawCoverageMask()[rejected, accepted],
            Renderer.DrawDistanceField()[rejected, accepted],
            Renderer.DrawMesh()[rejected, accepted],
            Renderer.DrawPaintGraph()[rejected, accepted]];
        using RendererBuildHost2D host = declaration.CreateHost();
        var device = new ProfilingDevice();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var sampler = new DispatchSampler();
        var canvas = new Canvas2D();
        var paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));
        var rect = new Rect2D(0, 0, 4, 4);
        canvas.Begin();
        canvas.FillRect(rect, paint);
        canvas.FillRoundedRect(rect, 1, paint);
        canvas.FillCircle(new(2), 2, paint);
        canvas.DrawLine(default, new(4), paint, new());
        canvas.FillPath(Triangle(), paint);
        canvas.StrokePath(Triangle(), paint, new());
        canvas.DrawImage(new Image2D(view, sampler), rect, rect);
        using var coverage = new DispatchTexture { Format = TextureFormat.R8Unorm };
        using IGraphicsTextureView coverageView = coverage.CreateView();
        canvas.DrawCoverageMask(new CoverageImage2D(coverageView, sampler), rect, rect, paint);
        canvas.DrawDistanceField(new DistanceField2D(coverageView, sampler, DistanceFieldEncoding2D.Sdf, 4), rect, rect, paint);
        canvas.DrawMesh(new Mesh2D([new(default, 1), new(new(4, 0), 1), new(new(0, 4), 1)], [0, 1, 2]), paint);
        canvas.DrawPaintGraph(paint);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.Equal(11, backend.Commands.Count);
        Assert.Equal(11, rejected.Processors.Sum(processor => processor.Probes));
        Assert.Equal(11, accepted.Processors.Sum(processor => processor.Probes));
        Assert.Equal(11, rejected.Creates);
        Assert.Equal(11, accepted.Creates);
    }

    private sealed class AllKindAdmissionRecipe(bool accepts, DispatchRenderers backend) : PipelineDeclaration2D,
        IPipelineDeclaration2D<FillRectCommand2D>,
        IPipelineDeclaration2D<FillRoundedRectCommand2D>,
        IPipelineDeclaration2D<FillCircleCommand2D>,
        IPipelineDeclaration2D<DrawLineCommand2D>,
        IPipelineDeclaration2D<FillPathCommand2D>,
        IPipelineDeclaration2D<StrokePathCommand2D>,
        IPipelineDeclaration2D<DrawImageCommand2D>,
        IPipelineDeclaration2D<DrawCoverageMaskCommand2D>,
        IPipelineDeclaration2D<DrawDistanceFieldCommand2D>,
        IPipelineDeclaration2D<DrawMeshCommand2D>,
        IPipelineDeclaration2D<DrawPaintGraphCommand2D>
    {
        public override Type ProcessorType => typeof(AllKindAdmissionProcessor);

        internal int Creates { get; private set; }

        internal List<AllKindAdmissionProcessor> Processors { get; } = [];

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            Creates++;
            var processor = new AllKindAdmissionProcessor(accepts, backend);
            Processors.Add(processor);
            return processor;
        }
    }

    private sealed class AllKindAdmissionProcessor(bool accepts, DispatchRenderers backend) : PipelineElement2D,
        IFillRectRenderer2D,
        IPipelineAdmission2D<FillRectCommand2D>,
        IFillRoundedRectRenderer2D,
        IPipelineAdmission2D<FillRoundedRectCommand2D>,
        IFillCircleRenderer2D,
        IPipelineAdmission2D<FillCircleCommand2D>,
        IDrawLineRenderer2D,
        IPipelineAdmission2D<DrawLineCommand2D>,
        IFillPathRenderer2D,
        IPipelineAdmission2D<FillPathCommand2D>,
        IStrokePathRenderer2D,
        IPipelineAdmission2D<StrokePathCommand2D>,
        IDrawImageRenderer2D,
        IPipelineAdmission2D<DrawImageCommand2D>,
        IDrawCoverageMaskRenderer2D,
        IPipelineAdmission2D<DrawCoverageMaskCommand2D>,
        IDrawDistanceFieldRenderer2D,
        IPipelineAdmission2D<DrawDistanceFieldCommand2D>,
        IDrawMeshRenderer2D,
        IPipelineAdmission2D<DrawMeshCommand2D>,
        IDrawPaintGraphRenderer2D,
        IPipelineAdmission2D<DrawPaintGraphCommand2D>
    {
        internal int Probes { get; private set; }

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default) => ((IFillRectRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default) => ((IFillRoundedRectRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default) => ((IFillCircleRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawLineRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default) => ((IFillPathRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default) => ((IStrokePathRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawImageRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawCoverageMaskRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawDistanceFieldRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawMeshRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands)
        {
            Probes++;
            return accepts;
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawPaintGraphRenderer2D)backend).PrepareAsync(context, commands, cancellationToken);
    }

    private sealed class NestedRectangleRecipe(PipelineDeclaration2D child) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(ServiceProcessor<SharedBackend>);

        internal ServiceProcessor<SharedBackend>? Instance { get; private set; }

        protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => [child];

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            Instance = context.GetChild<ServiceProcessor<SharedBackend>>(0);
            return new ServiceProcessor<SharedBackend>(Instance.Manager);
        }
    }

    private sealed class InvalidNestedRecipe(bool cycle) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(SharedBackend);

        internal int Creates { get; private set; }

        protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => cycle ? [this] : [new WrongCapabilityRecipe()];

        protected override object CreateElement(PipelineBuildContext2D context)
        {
            Creates++;
            return new ServiceProcessor<SharedBackend>(new SharedBackend());
        }
    }

    private sealed class WrongCapabilityRecipe : PipelineDeclaration2D, IPipelineDeclaration2D<FillCircleCommand2D>
    {
        public override Type ProcessorType => typeof(DefaultFillRectRenderer2D);

        protected override object CreateElement(PipelineBuildContext2D context) => throw new InvalidOperationException("must preflight");
    }

    private sealed class CircleAdmissionRecipe(IFillCircleRenderer2D backend) : PipelineDeclaration2D, IPipelineDeclaration2D<FillCircleCommand2D>
    {
        public override Type ProcessorType => typeof(CircleAdmissionProcessor);

        protected override object CreateElement(PipelineBuildContext2D context) => new CircleAdmissionProcessor(backend);
    }

    private sealed class CircleAdmissionProcessor(IFillCircleRenderer2D backend) : PipelineElement2D, IFillCircleRenderer2D, IPipelineAdmission2D<FillCircleCommand2D>
    {
        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands) => throw new InvalidOperationException("unrelated kind probed");

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default) => backend.PrepareAsync(context, commands, cancellationToken);
    }
}
