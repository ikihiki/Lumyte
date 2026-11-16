using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks raw scene encoding, bounded work and the independent compute/resolve wire contracts.</summary>
public sealed class ComputeExecutionTests
{
    /// <summary>Original curve controls are encoded once; no flattened geometry reaches CPU paint payloads.</summary>
    [Fact]
    public void CurveEncodingDoesNotInvokeCpuPathLowering()
    {
        using var fixture = new EncodingFixture();
        var builder = new PathBuilder2D();
        builder.MoveTo(new(2, 2));
        builder.CubicTo(new(3, 12), new(14, 3), new(14, 14));
        builder.Close();
        var command = new FillPathCommand2D(builder.Build(), Paint(), FillRule2D.EvenOdd, State());
        ComputeExecutionPlan2D plan = fixture.Encode([command]);
        DrawRecord2D record = Assert.Single(plan.Page.Records);
        Assert.Equal(9u, record.Kind);
        Assert.Equal(0u, record.PayloadCount);
        Assert.Single(plan.Page.Numeric); // Paint only: no CPU-generated line/triangle arrays.
        Assert.Equal(1u, plan.Configuration[0]);
        Assert.Equal(3u, plan.Configuration[1]);
        int curves = 4 + 8;
        Assert.Equal(new Vector4(2, 2, 2, 2), plan.Input[curves]);
        Assert.Equal(new Vector4(2, 2, 3, 12), plan.Input[curves + 3]);
        Assert.Equal(new Vector4(14, 3, 14, 14), plan.Input[curves + 4]);
        Assert.Equal((uint)PathVerb2D.Cubic, Bits(plan.Input[curves + 5].X));
        Assert.True(Bits(plan.Input[curves + 5].Y) > 1);
        Assert.Equal(0, fixture.Device.NativeBuffers);
    }

    /// <summary>Analytic shapes stay in the same compute scene and preserve their ordinal and paint identities.</summary>
    [Fact]
    public void MixedShapesStayInOneOrderedScene()
    {
        using var fixture = new EncodingFixture();
        DrawingCommand2D[] commands =
        [
            new FillRectCommand2D(new(0, 0, 8, 8), Paint(), State()),
            new FillCircleCommand2D(new(8, 8), 4, PaintGraph2D.Solid(SRgba<float>.Blue), State()),
            new FillRoundedRectCommand2D(new(4, 4, 8, 8), 2, Paint(), State()),
        ];
        ComputeExecutionPlan2D plan = fixture.Encode(commands);
        Assert.Equal(new uint[] { 1, 3, 2 }, plan.Page.Records.Select(record => record.Kind));
        Assert.Equal(3u, plan.Configuration[0]);
        Assert.Equal(0u, plan.Configuration[1]);
        Assert.Equal(0u, plan.Configuration[4]);
        Assert.True(plan.Configuration[7] >= 3);
        Assert.Equal(4 + (3 * 8), plan.Input.Count);
        Assert.NotEqual(plan.Page.Records[0].PaintId, plan.Page.Records[1].PaintId);
        Assert.Equal(plan.Page.Records[0].PaintId, plan.Page.Records[2].PaintId);
        Assert.Equal(0, fixture.Device.NativeBuffers);
    }

    /// <summary>All cap/join styles reserve GPU output without populating the CPU numeric stream with triangles.</summary>
    /// <param name="cap">The open-end style.</param>
    /// <param name="join">The internal join style.</param>
    [Theory]
    [InlineData(LineCap2D.Butt, LineJoin2D.Miter)]
    [InlineData(LineCap2D.Square, LineJoin2D.Bevel)]
    [InlineData(LineCap2D.Round, LineJoin2D.Round)]
    public void StrokeReservesGpuExpansion(LineCap2D cap, LineJoin2D join)
    {
        using var fixture = new EncodingFixture();
        var builder = new PathBuilder2D();
        builder.MoveTo(new(3, 3));
        builder.QuadraticTo(new(8, 12), new(12, 4));
        builder.LineTo(new(10, 10));
        var style = new StrokeStyle2D { Width = 2, Cap = cap, Join = join };
        ComputeExecutionPlan2D plan = fixture.Encode([new StrokePathCommand2D(builder.Build(), Paint(), style, State())]);
        Assert.Single(plan.Page.Numeric);
        Assert.Equal(9u, Assert.Single(plan.Page.Records).Kind);
        Assert.Equal(1u, Bits(plan.Input[4 + 6].X));
        Assert.Equal(2, plan.Input[4 + 5].X);
        Assert.Equal((float)cap, plan.Input[4 + 5].Z);
        Assert.Equal((float)join, plan.Input[4 + 5].W);
        Assert.True(Bits(plan.Input[4 + 1].Y) > 2);
        Assert.True(plan.Configuration[6] > 0);
        Assert.True(plan.ColorOffset > 0);
        Assert.Equal(0, fixture.Device.NativeBuffers);
    }

    /// <summary>Sample-row backdrop storage includes tiles outside the target, so an empty interior bin still has winding.</summary>
    [Fact]
    public void OffscreenContourReservesBackdropThroughInteriorTiles()
    {
        using var fixture = new EncodingFixture();
        var builder = new PathBuilder2D();
        builder.MoveTo(new(-16, -16));
        builder.LineTo(new(48, -16));
        builder.LineTo(new(48, 48));
        builder.LineTo(new(-16, 48));
        builder.Close();
        ComputeExecutionPlan2D plan = fixture.Encode([new FillPathCommand2D(builder.Build(), Paint(), FillRule2D.NonZero, State())]);
        Vector4 tileBounds = plan.Input[4 + 2];
        Assert.True(unchecked((int)Bits(tileBounds.X)) < 0);
        Assert.True(unchecked((int)Bits(tileBounds.Y)) < 0);
        Assert.True(plan.Configuration[4] > plan.Configuration[5]);
        Assert.Equal(plan.Configuration[12] + (plan.Configuration[4] * 64), plan.Configuration[13]);
        Assert.True(plan.BackdropRows >= 64);
    }

    /// <summary>Mesh center-interpolated coverage is retained as the mesh record kind, not converted to binary membership.</summary>
    [Fact]
    public void MeshKeepsCenterCoverageEvaluator()
    {
        using var fixture = new EncodingFixture();
        var mesh = new Mesh2D([new(new(1, 1), 0.25f), new(new(12, 1), 0.5f), new(new(1, 12), 1)], [0, 1, 2]);
        ComputeExecutionPlan2D plan = fixture.Encode([new DrawMeshCommand2D(mesh, Paint(), State())]);
        Assert.Equal(6u, Assert.Single(plan.Page.Records).Kind);
        Assert.Equal(0u, plan.Configuration[4]);
        Assert.True(plan.Page.Numeric.Count > 1);
    }

    /// <summary>Clip contours become CPU-authoritative sample input, never GPU curve geometry.</summary>
    [Fact]
    public void NormalizedClipSamplesRemainSeparateFromGpuGeometry()
    {
        using var fixture = new EncodingFixture();
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new(10, 0));
        builder.LineTo(new(0, 10));
        builder.Close();
        var clip = new ClipStack2D([new(builder.Build(), Matrix3x2.Identity, FillRule2D.NonZero)]);
        DrawingState2D state = State() with { Clip = clip };
        ComputeExecutionPlan2D plan = fixture.Encode([new FillRectCommand2D(new(0, 0, 12, 12), Paint(), state)]);
        Assert.NotEqual(0u, Assert.Single(plan.Page.Records).ClipId);
        Assert.True(plan.Page.Clips.Count > 1);
        Assert.True(plan.Page.Numeric.Count > 1);
        Assert.Equal(0u, plan.Configuration[1]);
        Assert.Equal(0u, plan.Configuration[4]);
        Assert.Equal(plan.Configuration[15] + plan.Configuration[7], plan.WorkCount);
        Assert.Equal(0u, plan.ColorOffset % 16); // 256-byte storage-offset alignment.
    }

    /// <summary>Empty path input cannot accidentally become a full-target DrawPaintGraph operation.</summary>
    [Fact]
    public void EmptyPathsStayTransparent()
    {
        using var fixture = new EncodingFixture();
        Path2D path = new PathBuilder2D().Build();
        ComputeExecutionPlan2D plan = fixture.Encode([new FillPathCommand2D(path, Paint(), FillRule2D.NonZero, State())]);
        Assert.Empty(plan.Page.Records);
        Assert.Equal(0u, plan.Configuration[0]);
        Assert.Equal(0u, plan.Configuration[7]);
        Assert.Equal(32u * 32u, plan.PixelCount);
        Assert.True(plan.StorageCount >= plan.PixelCount);
    }

    /// <summary>Raw transforms preserve DPR and nonzero output origin instead of rounding the logical command.</summary>
    [Fact]
    public void RawCurveTransformContainsDprAndPixelOrigin()
    {
        using var fixture = new EncodingFixture(pixelScale: 2, origin: new(5, 7));
        var builder = new PathBuilder2D();
        builder.MoveTo(new(4, 5));
        builder.LineTo(new(8, 5));
        builder.LineTo(new(4, 9));
        builder.Close();
        DrawingState2D state = State() with { Transform = Matrix3x2.CreateTranslation(1, 2) };
        ComputeExecutionPlan2D plan = fixture.Encode([new FillPathCommand2D(builder.Build(), Paint(), FillRule2D.NonZero, state)]);
        Assert.Equal(new Vector4(2, 0, -3, 0), plan.Input[4 + 3]);
        Assert.Equal(new Vector4(0, 2, -3, 0), plan.Input[4 + 4]);
        Assert.Equal(new Vector4(4, 5, 4, 5), plan.Input[4 + 8]);
    }

    /// <summary>Unsupported commands, cancellation and byte exhaustion fail before any GPU allocation or hidden fallback.</summary>
    [Fact]
    public void FailureBoundariesDoNotAllocateOrFallback()
    {
        using var fixture = new EncodingFixture();
        Assert.Throws<NotSupportedException>(() => fixture.Encode([new CustomCommand(State())]));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => fixture.Encode([new FillRectCommand2D(new(0, 0, 8, 8), Paint(), State())], cancelled.Token));
        using var tiny = new EncodingFixture(maxWorkingBytes: 1024);
        Assert.Throws<NotSupportedException>(() => tiny.Encode([]));
        Assert.Equal(0, fixture.Device.NativeBuffers);
        Assert.Equal(0, tiny.Device.NativeBuffers);
    }

    /// <summary>Integer tile boundaries do not add a speculative next tile to candidate capacity.</summary>
    [Fact]
    public void CandidateReservationsUseHalfOpenClippedBounds()
    {
        Assert.Equal(1, ComputeExecutionPlan2D.CandidateTiles(new(0, 0, 16, 16), 32, 32));
        Assert.Equal(4, ComputeExecutionPlan2D.CandidateTiles(new(0, 0, 17, 17), 32, 32));
        Assert.Equal(1, ComputeExecutionPlan2D.CandidateTiles(new(-16, -16, 16, 16), 32, 32));
        Assert.Equal(0, ComputeExecutionPlan2D.CandidateTiles(new(32, 32, 64, 64), 32, 32));
    }

    /// <summary>Compute dispatch validation rejects insufficient invocation and workgroup capacities.</summary>
    [Fact]
    public void DispatchesRespectDeviceCapsWithoutClamping()
    {
        DeviceCaps caps = Caps();
        Assert.Equal(1u, ComputeExecutionPlan2D.Groups(256, caps));
        Assert.Equal(2u, ComputeExecutionPlan2D.Groups(257, caps));
        Assert.Throws<NotSupportedException>(() => ComputeExecutionPlan2D.Groups(1, caps with { MaxComputeInvocationsPerWorkgroup = 255 }));
        Assert.Throws<NotSupportedException>(() => ComputeExecutionPlan2D.Groups(513, caps with { MaxComputeWorkgroupsPerDimension = 2 }));
    }

    /// <summary>Every offline backend exposes the compute root itself, not an included fragment root with a different ABI.</summary>
    /// <param name="target">The offline target.</param>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.Dxil)]
    public void OfflineArtifactsUseIndependentComputeAndResolveSchemas(ShaderTarget target)
    {
        var artifact = ShaderArtifact.LoadEmbedded(typeof(Renderer2D).Assembly, "Lumyte.Drawing2D.tiled-scene-compute.lshader");
        ComputeExecutionArguments2D compute = default;
        ShaderDataLayout.Data(artifact, target, nameof(ComputeExecutionArguments2D)).ValidateTransferLayout(96, ShaderTransfer.Fields(in compute), nameof(ComputeExecutionArguments2D));
        DrawRecord2D record = default;
        ShaderDataLayout.Data(artifact, target, nameof(DrawRecord2D)).ValidateTransferLayout(96, ShaderTransfer.Fields(in record), nameof(DrawRecord2D));
        DrawPageMetadata2D metadata = default;
        ShaderDataLayout.Data(artifact, target, nameof(DrawPageMetadata2D)).ValidateTransferLayout(80, ShaderTransfer.Fields(in metadata), nameof(DrawPageMetadata2D));
        foreach (string stage in new[] { "vertex", "fragment" })
        {
            var resolveArtifact = ShaderArtifact.LoadEmbedded(typeof(Renderer2D).Assembly, "Lumyte.Drawing2D.tiled-resolve-" + stage + ".lshader");
            ComputeExecutionResolveArguments2D resolve = default;
            ShaderDataLayout.Data(resolveArtifact, target, nameof(ComputeExecutionResolveArguments2D)).ValidateTransferLayout(32, ShaderTransfer.Fields(in resolve), nameof(ComputeExecutionResolveArguments2D));
        }
    }

    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    private static PaintGraph2D Paint() => PaintGraph2D.Solid(SRgba<float>.Red);

    private static DrawingState2D State() => new(Matrix3x2.Identity, ClipStack2D.Empty);

    private static DeviceCaps Caps() => new()
    {
        MaxBufferSize = 1UL << 30,
        MaxStorageBufferBindingSize = 1UL << 30,
        MaxComputeInvocationsPerWorkgroup = 256,
        MaxComputeWorkgroupSizeX = 16,
        MaxComputeWorkgroupSizeY = 16,
        MaxComputeWorkgroupSizeZ = 1,
        MaxComputeWorkgroupsPerDimension = 65535,
        MaxTextureDimension2D = 4096,
        StorageBufferOffsetAlignment = 256,
    };

    private sealed class EncodingFixture : IDisposable
    {
        private readonly DrawingArgumentPlan2D _arguments;
        private readonly DrawingResourceState2D _resources;
        private readonly RenderPreparationContext2D _context;

        internal EncodingFixture(long maxWorkingBytes = 256L * 1024 * 1024, float pixelScale = 1, Vector2 origin = default)
        {
            Device = new() { Caps = Caps() };
            _arguments = new(Device);
            _resources = new(Device);
            var target = new DrawingTargetInfo2D
            {
                Description = new() { Width = 32, Height = 32, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment },
                PixelScale = pixelScale,
                PixelOrigin = origin,
            };
            _context = new(Device, target, _resources, _arguments) { MaxWorkingBytes = maxWorkingBytes };
        }

        internal ProfilingDevice Device { get; }

        public void Dispose()
        {
            _arguments.Dispose();
            _resources.Dispose();
        }

        internal ComputeExecutionPlan2D Encode(DrawingCommand2D[] commands, CancellationToken token = default)
            => ComputeExecutionPlan2D.Encode(_context, commands, _arguments.Reserve(), token);
    }

    private sealed record CustomCommand(DrawingState2D State) : DrawingCommand2D(State);
}
