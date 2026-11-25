using System.Numerics;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Canonical CPU row-mask coverage regression tests.</summary>
public sealed class CpuSparseCoverageTests
{
    private static readonly DrawingState2D _state = new(Matrix3x2.Identity, ClipStack2D.Empty);
    private static readonly PaintGraph2D _paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));

    /// <summary>Inclusive rounded boundaries do not become half-open rectangles.</summary>
    [Fact]
    public void RoundedZeroRectAndStrictCircleKeepDifferentBoundaryCounts()
    {
        var rect = new Rect2D(.125f, .125f, .5f, .5f);
        Assert.Equal(9, Run(new FillRoundedRectCommand2D(rect, 0, _paint, _state)).Sum());
        Assert.Equal(4, Run(new FillRectCommand2D(rect, _paint, _state)).Sum());
        Assert.Equal(1, Run(new FillCircleCommand2D(new(.375f, .375f), .25f, _paint, _state)).Sum());
    }

    /// <summary>Fractional mesh center coverage remains fractional and sample invariant.</summary>
    [Fact]
    public void MeshUsesOneFractionalCenterNotSixteenMembershipSamples()
    {
        var mesh = new Mesh2D([new(new(0, 0), .25f), new(new(2, 0), .5f), new(new(0, 2), .75f)], [0, 1, 2]);
        float[] values = Run(new DrawMeshCommand2D(mesh, _paint, _state));
        Assert.All(values, value => Assert.Equal(.4375f, value));
    }

    /// <summary>Malformed root and insufficient budgets reject before output publication.</summary>
    [Fact]
    public void MalformedRootAndBudgetReject()
    {
        RenderRequest2D request = Request(new FillRectCommand2D(new(0, 0, 1, 1), _paint, _state));
        Vector4[] cells = CpuNormalize2D.Normalize(request, new(2048, 65536, 262144, 1048576), default);
        var extent = new SchemaExtent(cells.Length, request.Metadata());
        Assert.Throws<NotSupportedException>(() => CpuSparseCoverage2D.Evaluate(cells, request.FrameCells(), extent, 1, default));
        cells[6] = new(BitConverter.UInt32BitsToSingle(256), cells[6].Y, cells[6].Z, cells[6].W);
        Assert.Throws<InvalidOperationException>(() => CpuSparseCoverage2D.Evaluate(cells, request.FrameCells(), extent, 1024 * 1024, default));
    }

    /// <summary>Checks per-preparation translation without changing canonical geometry bytes.</summary>
    [Fact]
    public void PlacementSubtractsCoordinatesAndLeavesSourceUnchanged()
    {
        RenderRequest2D request = Request(new FillRectCommand2D(new(-1, 0, 1, 1), _paint, _state));
        Vector4[] cells = CpuNormalize2D.Normalize(request, new(2048, 65536, 262144, 1048576), default);
        Vector4[] original = cells.ToArray();
        var metadata = new Dictionary<string, object>(request.Metadata()) { ["translationX"] = 1f, ["translationY"] = 0f };
        float[] values = CpuSparseCoverage2D.Evaluate(cells, request.FrameCells(), new(cells.Length, metadata), 1024 * 1024, default);
        Assert.All(values, value => Assert.Equal(1, value));
        Assert.Equal(original, cells);
    }

    /// <summary>Exercises actual compiled processors with a caller working limit far below the global maximum.</summary>
    /// <param name="limit">The stage-specific working byte ceiling.</param>
    /// <returns>The completed CPU preparation.</returns>
    [Theory]
    [InlineData(1024)]
    [InlineData(1048576)]
    public async Task CompiledSmallBudgetPublishesImmutableCoverageAsync(int limit)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            var graph = new PipelineGraph<RenderRequest2D>();
            Port<Host<SceneV1>> scene = graph.Stage(new SceneProducer(), graph.Input);
            Port<Host<FrameV1>> frame = graph.Stage(RenderFields2D.Frame(), graph.Input);
            Port<Host<Coverage16V1>> coverage = graph.Stage(new CpuSparseCoverage2D(new(16, 1), limit), scene, frame);
            Port<float> output = coverage.Then(new CoverageSum());
            using var build = new PipelineBuildHost(runtime, new(8, 16 * 1024, 3));
            using IPipeline<RenderRequest2D, float> pipeline = build.Build(graph.Complete(output));
            RenderRequest2D request = Request(new FillRoundedRectCommand2D(new(.125f, .125f, .5f, .5f), 0, _paint, _state));
            Task<PipelineResult<float>> prepare = pipeline.PrepareAsync(request).AsTask();
            Pump(runtime, prepare);
            using PipelineResult<float> result = await prepare;
            Assert.Equal(9, result.Value);
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    /// <summary>Explicit sparse dimension bounds apply even when zero commands leave the sample count zero.</summary>
    [Fact]
    public void EmptyScenesStillEnforceRowBounds()
    {
        var target = new DrawingTargetInfo2D { Description = new TextureDesc { Width = 65536, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment } };
        var request = new RenderRequest2D(target, [], Guid.NewGuid());
        Vector4[] cells = CpuNormalize2D.Normalize(request, new(16, 64, 64, 64), default);
        Assert.Throws<NotSupportedException>(() => CpuSparseCoverage2D.Evaluate(cells, request.FrameCells(), new(cells.Length, request.Metadata()), 1024 * 1024, default));
    }

    /// <summary>Actual source metadata cannot select a build-only minimum reservation.</summary>
    [Fact]
    public void CanonicalMetadataCannotMasqueradeAsMinimumDescription()
    {
        RenderRequest2D request = Request(new FillRectCommand2D(new(0, 0, 1, 1), _paint, _state));
        Dictionary<string, object> metadata = request.Metadata();
        metadata["LowerBound"] = true;
        var extent = new SchemaExtent(1, metadata);
        var descriptions = (StageInputDescriptions)Activator.CreateInstance(typeof(StageInputDescriptions), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { new[] { extent, extent }, long.MaxValue }, null)!;
        StageRequirements requirement = new CpuSparseCoverage2D(new(16, 1), 1024).Factory.Describe(descriptions);
        Assert.Equal(386, requirement.MaxHostBytes);
    }

    /// <summary>Every sample of every command survives the exact large two-page paging fixture.</summary>
    [Fact]
    public void TwoPageScenePreservesAllCommandMasks()
    {
        DrawingCommand2D[] commands = Enumerable.Range(0, 256).Select(i => (DrawingCommand2D)new FillRectCommand2D(new(.25f, .25f, 256.5f, .5f), _paint, _state)).ToArray();
        var target = new DrawingTargetInfo2D { Description = new TextureDesc { Width = 257, Height = 1, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } };
        var request = new RenderRequest2D(target, commands, Guid.NewGuid());
        Vector4[] cells = CpuNormalize2D.Normalize(request, new(2048, 65536, 262144, 1048576), default);
        Vector4[] original = cells.ToArray();
        Assert.InRange(cells.Length * 16, 65537, 131072);
        float[] values = CpuSparseCoverage2D.Evaluate(cells, request.FrameCells(), new(cells.Length, request.Metadata()), 256L * 1024 * 1024, default);
        Assert.Equal(257 * 256 * 16, values.Length);
        for (int command = 0; command < 256; command++)
        {
            for (int x = 0; x < 257; x++)
            {
                for (int sample = 0; sample < 16; sample++)
                {
                    float px = x + (((sample % 4) + .5f) / 4), py = ((sample / 4) + .5f) / 4;
                    float expected = px >= .25f && px < 256.75f && py >= .25f && py < .75f ? 1 : 0;
                    Assert.Equal(expected, values[(((command * 257) + x) * 16) + sample]);
                }
            }
        }

        Assert.Equal(original, cells);
    }

    private static void Pump(ExecutionRuntime runtime, Task task)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (timeout.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("CPU sparse stage gate timed out.");
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private static float[] Run(DrawingCommand2D command)
    {
        RenderRequest2D request = Request(command);
        Vector4[] cells = CpuNormalize2D.Normalize(request, new(2048, 65536, 262144, 1048576), default);
        return CpuSparseCoverage2D.Evaluate(cells, request.FrameCells(), new(cells.Length, request.Metadata()), 1024 * 1024, default);
    }

    private static RenderRequest2D Request(DrawingCommand2D command) => new(
        new DrawingTargetInfo2D
        {
            Description = new TextureDesc { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment },
        },
        [command],
        Guid.NewGuid());

    private sealed class SceneProducer : IStage<RenderRequest2D, Host<SceneV1>>, IStageFactory<RenderRequest2D, Host<SceneV1>>, IStageProcessor<RenderRequest2D, Host<SceneV1>>
    {
        public IStageFactory<RenderRequest2D, Host<SceneV1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(4096, []);

        public IStageProcessor<RenderRequest2D, Host<SceneV1>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<SceneV1>>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> input, CancellationToken cancellationToken)
        {
            RenderRequest2D request = context.Read(input);
            Vector4[] cells = CpuNormalize2D.Normalize(request, new(16, 64, 64, 64), cancellationToken);
            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<SceneV1, Vector4>(cells, new(cells.Length, request.Metadata()))));
        }

        public void Dispose()
        {
        }
    }

    private sealed class CoverageSum : IStage<Host<Coverage16V1>, float>, IStageFactory<Host<Coverage16V1>, float>, IStageProcessor<Host<Coverage16V1>, float>
    {
        public IStageFactory<Host<Coverage16V1>, float> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(256, []);

        public IStageProcessor<Host<Coverage16V1>, float> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<float>> PrepareAsync(StagePreparationContext context, ValueLease<Host<Coverage16V1>> input, CancellationToken cancellationToken)
        {
            float sum = 0;
            foreach (float sample in context.ReadHost(input).ReadElements<float>())
            {
                sum += sample;
            }

            return ValueTask.FromResult(context.Publish(sum));
        }

        public void Dispose()
        {
        }
    }
}
