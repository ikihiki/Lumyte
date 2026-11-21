using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises real canonical CPU stage behavior without native GPU execution.</summary>
public sealed class CanonicalNormalizeTests
{
    private static readonly SceneLimits2D _limits = new(2048, 65536, 262144, 1048576);
    private static readonly DrawingState2D _state = new(Matrix3x2.Identity, ClipStack2D.Empty);
    private static readonly PaintGraph2D _paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));

    private static DrawingTargetInfo2D Target => new() { Description = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment } };

    /// <summary>Rect Has Real Program And Stable Invisible Ordinals.</summary>
    [Fact]
    public void RectHasRealProgramAndStableInvisibleOrdinals()
    {
        Vector4[] cells = Run(new FillRectCommand2D(new(0, 0, 8, 8), _paint, _state), new FillRectCommand2D(new(0, 0, 8, 8), _paint, _state with { Transform = default(Matrix3x2) }));
        Assert.Equal(0x314E4353u, U(cells[0].X));
        Assert.Equal(2u, U(cells[0].Y));
        Assert.Equal(0u, U(cells[1].X));
        int second = (int)U(cells[1].Z);
        Assert.Equal(1u, U(cells[second].X));
        Assert.Equal(1u, U(cells[second].Y));
        int program = (int)U(cells[2].X);
        Assert.Equal(1u, U(cells[program + 6].X));
        Assert.Equal(new Vector4(0, 0, 8, 8), cells[program + 2]);
    }

    /// <summary>Curves And Round Strokes Are Actually Expanded.</summary>
    [Fact]
    public void CurvesAndRoundStrokesAreActuallyExpanded()
    {
        var b = new PathBuilder2D();
        b.MoveTo(new(0, 0));
        b.CubicTo(new(0, 12), new(12, 12), new(12, 0));
        Vector4[] cells = Run(new StrokePathCommand2D(b.Build(), _paint, new() { Width = 2, Cap = LineCap2D.Round, Join = LineJoin2D.Round }, _state));
        int instruction = (int)U(cells[2].X);
        Assert.Equal(5u, U(cells[instruction + 6].X));
        Assert.True(U(cells[instruction + 6].Z) > 4);
        Assert.True(U(cells[2].W) > 12);
    }

    /// <summary>Mesh Retains Nonbinary Center Coverage Metadata And Vertices.</summary>
    [Fact]
    public void MeshRetainsNonbinaryCenterCoverageMetadataAndVertices()
    {
        var mesh = new Mesh2D([new(new(0, 0), .25f), new(new(8, 0), .5f), new(new(0, 8), .75f)], [0, 1, 2]);
        Vector4[] cells = Run(new DrawMeshCommand2D(mesh, _paint, _state));
        Assert.Equal(1u, U(cells[3].W));
        int instruction = (int)U(cells[2].X);
        Assert.Equal(1u, U(cells[instruction + 6].W));
        int data = (int)U(cells[2].Z);
        Assert.Equal(.25f, cells[data].Z);
        Assert.Equal(.75f, cells[data + 2].Z);
    }

    /// <summary>All Eleven Kinds Pack Real Programs And Separate Resources.</summary>
    [Fact]
    public void AllElevenKindsPackRealProgramsAndSeparateResources()
    {
        var b = new PathBuilder2D();
        b.MoveTo(new(0, 0));
        b.LineTo(new(8, 0));
        b.LineTo(new(0, 8));
        b.Close();
        Path2D path = b.Build();
        var sampler = new BorrowedSampler();
        var color = new BorrowedView(TextureFormat.Rgba8Unorm);
        var mask = new BorrowedView(TextureFormat.R8Unorm);
        Rect2D rect = new(0, 0, 8, 8);
        DrawingCommand2D[] commands =
        [
            new FillRectCommand2D(rect, _paint, _state), new FillRoundedRectCommand2D(rect, 2, _paint, _state), new FillCircleCommand2D(new(4, 4), 3, _paint, _state),
            new DrawLineCommand2D(new(0, 0), new(8, 8), _paint, new(), _state), new FillPathCommand2D(path, _paint, FillRule2D.NonZero, _state), new StrokePathCommand2D(path, _paint, new(), _state),
            new DrawImageCommand2D(new(color, sampler), rect, rect, _state), new DrawCoverageMaskCommand2D(new(mask, sampler), rect, rect, _paint, _state),
            new DrawDistanceFieldCommand2D(new(mask, sampler, DistanceFieldEncoding2D.Sdf, 4), rect, rect, _paint, _state),
            new DrawMeshCommand2D(new([new(new(0, 0), .25f), new(new(8, 0), .5f), new(new(0, 8), .75f)], [0, 1, 2]), _paint, _state), new DrawPaintGraphCommand2D(_paint, _state),
        ];
        var request = new RenderRequest2D(Target, commands, Guid.NewGuid());
        Vector4[] cells = CpuNormalize2D.Normalize(request, _limits, default);
        Assert.Equal(2, request.ResourceSnapshot.Views.Count);
        Assert.Single(request.ResourceSnapshot.Samplers);
        int record = 1;
        for (uint kind = 0; kind < 11; kind++)
        {
            Assert.Equal(kind, U(cells[record].X));
            Assert.Equal(kind, U(cells[record + 5].Z));
            Assert.True(U(cells[record + 1].Y) > 0);
            record = (int)U(cells[record].Z);
        }

        Assert.Equal(cells.Length, record);
    }

    /// <summary>_limits Reject Before Canonical Scene Publication.</summary>
    [Fact]
    public void LimitsRejectBeforeCanonicalScenePublication()
    {
        var b = new PathBuilder2D();
        b.MoveTo(new(0, 0));
        b.LineTo(new(8, 0));
        b.LineTo(new(0, 8));
        b.Close();
        var r = new RenderRequest2D(Target, [new FillPathCommand2D(b.Build(), _paint, FillRule2D.NonZero, _state)], Guid.NewGuid());
        Assert.Throws<NotSupportedException>(() => CpuNormalize2D.Normalize(r, _limits with { MaxSegments = 1 }, default));
        Assert.Throws<NotSupportedException>(() => CpuNormalize2D.Normalize(r, _limits with { MaxContours = 0 }, default));
        Assert.Throws<NotSupportedException>(() => CpuNormalize2D.Normalize(r, _limits with { MaxTileReferences = 0 }, default));
    }

    /// <summary>Clip Mask Preserves All Correlated Cpu Bits.</summary>
    [Fact]
    public void ClipMaskPreservesAllCorrelatedCpuBits()
    {
        var b = new PathBuilder2D();
        b.MoveTo(new(.2f, .2f));
        b.LineTo(new(7.8f, .2f));
        b.LineTo(new(.2f, 7.8f));
        b.Close();
        var clips = new ClipStack2D([new(b.Build(), Matrix3x2.Identity, FillRule2D.NonZero)]);
        var command = new FillRectCommand2D(new(0, 0, 16, 16), _paint, _state with { Clip = clips });
        Vector4[] cells = Run(command);
        var oracle = NormalizedClip2D.Create(clips.Elements, Target);
        Assert.Equal(NormalizedClipKind2D.SampleMask, oracle.Kind);
        Assert.Equal((uint)oracle.Kind, U(cells[3].X));
        int offset = (int)U(cells[3].Y);
        Assert.Equal(oracle.Mask, cells.Skip(offset).ToArray());
    }

    /// <summary>Factory Pipeline Executes Host Projections And Normalization.</summary>
    /// <returns>The completed stage regression check.</returns>
    [Fact]
    public async Task FactoryPipelineExecutesHostProjectionsAndNormalizationAsync()
    {
        var runtime = new Lumyte.Execution.ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(Lumyte.Execution.OwnerPlacement.Host, Lumyte.Execution.CpuExecutionMode.Cooperative, 1, new Lumyte.Execution.ExecutionOptions());
        void Pump(Task task)
        {
            for (int i = 0; !task.IsCompleted && i < 100000; i++)
            {
                runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            }

            Assert.True(task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        Pump(runtime.StartAsync().AsTask());
        try
        {
            var g = new Lumyte.Pipelines.PipelineGraph<RenderRequest2D>();
            Port<Host<CommandsV1>> commands = g.Stage(RenderFields2D.Commands(), g.Input);
            Port<Host<ResourceManifestV1>> manifest = g.Stage(RenderFields2D.ResourceManifest(), g.Input);
            Port<Host<FrameV1>> frame = g.Stage(RenderFields2D.Frame(), g.Input);
            Port<Host<SceneV1>> scene = g.Stage(CpuStages2D.Normalize(new(16, 128, 512, 4)), commands, manifest, frame);
            using var build = new Lumyte.Pipelines.PipelineBuildHost(runtime, new(16, 512L * 1024 * 1024, 1));
            using IPipeline<RenderRequest2D, long> pipeline = build.Build(g.Complete(scene.Then(new InspectScene())));
            Task<PipelineResult<long>> pending = pipeline.PrepareAsync(new(Target, [new FillRectCommand2D(new(0, 0, 8, 8), _paint, _state)], Guid.NewGuid())).AsTask();
            Pump(pending);
            using PipelineResult<long> result = await pending;
            Assert.True(result.Value > 1);
        }
        finally
        {
            Pump(runtime.StopAsync().AsTask());
        }
    }

    /// <summary>Scene Wire Has No References And Fixed Sample Contract.</summary>
    [Fact]
    public void SceneWireHasNoReferencesAndFixedSampleContract()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Vector4>());
        Assert.Equal(typeof(Vector4), SceneV1.Descriptor.ElementType);
        Assert.Equal(16, SceneV1.Descriptor.ElementStride);
        Assert.Equal(16, SceneV1.Descriptor.Metadata["samples"]);
        Assert.Throws<NotSupportedException>(() => CpuStages2D.ValidateCoverage(new(8, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuStages2D.Normalize(new(-1, 1, 1, 1)));
    }

    private static uint U(float f) => BitConverter.SingleToUInt32Bits(f);

    private static Vector4[] Run(params DrawingCommand2D[] commands) => CpuNormalize2D.Normalize(new(Target, commands, Guid.NewGuid()), _limits, default);

    private sealed class InspectScene : IStage<Host<SceneV1>, long>, IStageFactory<Host<SceneV1>, long>, IStageProcessor<Host<SceneV1>, long>
    {
        public IStageFactory<Host<SceneV1>, long> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(128, []);

        public IStageProcessor<Host<SceneV1>, long> Create(StageBuildContext context) => new InspectScene();

        public ValueTask<ValueLease<long>> PrepareAsync(StagePreparationContext context, ValueLease<Host<SceneV1>> a, CancellationToken ct)
        {
            SchemaData<SceneV1> scene = context.ReadHost(a);
            Assert.False(scene.Extent.Metadata.ContainsKey("commandsSnapshot"));
            Assert.False(scene.Extent.Metadata.ContainsKey("resourceSnapshot"));
            Assert.Equal(0x314E4353u, U(scene.ReadElements<Vector4>()[0].X));
            return ValueTask.FromResult(context.Publish(scene.Extent.ElementCount));
        }

        public void Dispose()
        {
        }
    }

    private sealed class BorrowedSampler : IGraphicsSampler
    {
        public SamplerDesc Desc { get; } = new();

        public void Dispose() => throw new InvalidOperationException("CPU normalization must not release borrowed resources.");
    }

    private sealed class BorrowedView(TextureFormat format) : IGraphicsTextureView, IGraphicsTexture
    {
        public IGraphicsTexture Texture => this;

        public TextureViewInfo Info { get; } = new() { Format = format, Dimension = TextureViewDimension.D2, BaseMipLevel = 0, MipLevelCount = 1, BaseArrayLayer = 0, ArrayLayerCount = 1 };

        public uint Width => 8;

        public uint Height => 8;

        public uint MipLevels => 1;

        public uint ArrayLayers => 1;

        public TextureFormat Format => format;

        public TextureUsage Usage => TextureUsage.Sampled;

        public (uint Width, uint Height) GetMipSize(uint mip) => (8, 8);

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => throw new InvalidOperationException("No CPU stage may allocate a native view.");

        public void Dispose() => throw new InvalidOperationException("Borrowed resources remain separate.");
    }
}
