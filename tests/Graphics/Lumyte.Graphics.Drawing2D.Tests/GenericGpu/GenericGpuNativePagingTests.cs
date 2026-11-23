using System.Numerics;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Validation;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Full-target constrained native scene/sample paging, independent of upper coverage implementations.</summary>
public sealed partial class GenericGpuNativeTests
{
    /// <summary>Uses real normalization to prove both source and destination exceed a 64KiB range.</summary>
    [Fact]
    public void CanonicalPagingFixtureRequiresTwoScenePagesAnd257Windows()
    {
        var state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        DrawingCommand2D[] commands = Enumerable.Range(0, 256).Select(i => (DrawingCommand2D)new FillRectCommand2D(new(.25f, .25f, 256.5f, .5f), PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1)), state)).ToArray();
        var request = new RenderRequest2D(new() { Description = new() { Width = 257, Height = 1, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } }, commands, Guid.NewGuid());
        Vector4[] cells = CpuNormalize2D.Normalize(request, new(2048, 65536, 262144, 1048576), default);
        Assert.True(cells.Length * 16 > 65536);
        PagedGpuPreparation2D.Plan plan = PagedGpuPreparation2D.Admit(PagingCaps(), 257L * 256 * 16, cells.Length, new());
        Assert.Equal(2, plan.ScenePages);
        Assert.Equal(257, plan.Windows.Length);
        Assert.Equal(65792, 257 * 16 * 16);
    }

    /// <summary>Both native backends and parent Graph preserve every target pixel with producer-swapped coverage ports.</summary>
    /// <param name="wgpu">Selects actual native Wgpu instead of Vulkan.</param>
    /// <param name="graphMode">Selects the real parent Graph execution adapter.</param>
    /// <param name="cpuCoverage">Swaps only the canonical coverage producer.</param>
    [GenericGpuNativeTheory]
    [Trait("Execution", "Gpu")]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PagedSceneAndSamplesPreserveEveryPixel(bool wgpu, bool graphMode, bool cpuCoverage)
    {
        // Native RGBA16F blending may store with RTZ rather than System.Half's RTE.
        // Preserve an exact all-pixel legacy-native oracle instead of weakening a tolerance.
        byte[] expected = RenderPagingFixture(wgpu, graphMode, cpuCoverage, paged: false, bounded: false);
        byte[] actual = RenderPagingFixture(wgpu, graphMode, cpuCoverage, paged: true, bounded: true);
        Assert.Equal(expected, actual);
        string? evidence = Environment.GetEnvironmentVariable("LUMYTE_PAGED_PIXEL_EVIDENCE");
        if (evidence is not null)
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllBytes(Path.Combine(evidence, $"paged-{wgpu}-{graphMode}-{cpuCoverage}.rgba16f"), actual);
        }
    }

    /// <summary>The unchanged whole-range source binding genuinely fails the enforcing native wrapper.</summary>
    [GenericGpuNativeFact]
    [Trait("Execution", "Gpu")]
    public void WholeRangeLegacyFailsActual64KiBRegistration()
        => Assert.Throws<NotSupportedException>(() => RenderPagingFixture(false, false, false, paged: false, bounded: true));

    private static byte[] RenderPagingFixture(bool wgpu, bool graphMode, bool cpuCoverage, bool paged, bool bounded)
    {
        IGraphicDevice raw = wgpu ? Lumyte.Graphics.Wgpu.WgpuDevice.Create() : CreateDevice();
        using var rawOwner = (IDisposable)raw;
        using ValidationDevice? validation = bounded ? new ValidationDevice(new BoundedNativeDevice(raw)) : null;
        IGraphicDevice device = validation ?? raw;
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            var g = new PipelineGraph<RenderRequest2D>();
            Port<Device<SceneV1>> scene = g.Stage(new PagingCanonicalProducer(), g.Input).Then(Transfers.Upload<SceneV1>());
            Port<Device<FrameV1>> frame = g.Stage(RenderFields2D.Frame(), g.Input).Then(Transfers.Upload<FrameV1>());
            Port<Device<ResourceViewsV1>> views = g.Stage(RenderFields2D.ResourceViews(), g.Input);
            Port<Device<TargetV1>> targetPort = g.Stage(RenderFields2D.Target(), g.Input);
            var samples = new SamplePolicy2D(16, 1);
            var limits = new GenericPagingLimits2D();
            Port<Device<Coverage16V1>> coverage = cpuCoverage ? g.Stage(new PagingCpuCoverage(), g.Input).Then(Transfers.Upload<Coverage16V1>()) : paged ? g.Stage(ComputeStages2D.PagedCoverage(samples, limits), scene, frame) : g.Stage(ComputeStages2D.Coverage(samples), scene, frame);
            Port<Device<Paint16V1>> paint = paged ? g.Stage(ComputeStages2D.PagedPaint(samples, limits), scene, views, frame) : g.Stage(ComputeStages2D.Paint(samples), scene, views, frame);
            Port<Device<Clip16V1>> clip = paged ? g.Stage(ComputeStages2D.PagedClip(samples, limits), scene, frame) : g.Stage(ComputeStages2D.Clip(samples), scene, frame);
            Port<Device<TargetWritesV1>> output = paged ? g.Stage(RasterStages2D.PagedCompose(samples, limits), scene, coverage, paint, clip, targetPort) : g.Stage(RasterStages2D.Compose(samples), scene, coverage, paint, clip, targetPort);
            var policy = new GraphicsPipelinePolicy(1024L * 1024 * 1024, 128L * 1024 * 1024, 128L * 1024 * 1024, 3);
            using var build = new GraphicsPipelineBuildHost(runtime, device, Artifacts(), policy);
            using IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline = build.Build(g.Complete(output));
            using var renderer = new Renderer2D(device, pipeline, new(true, true, true, true, true, true, true, true, true, true, true), new(policy, DrawingArgumentTableMode.Auto, new()));
            var description = new TextureDesc { Width = 257, Height = 1, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource };
            using IGraphicsTexture texture = device.CreateTexture(description);
            using IGraphicsTextureView view = texture.CreateView();
            using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 2304, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            using var graph = new Graph(device);
            PreparedDraw2D? direct = null;
            PreparedDrawingPasses2D? preparedGraph = null;
            CompiledRenderGraph? compiled = null;
            try
            {
                if (graphMode)
                {
                    var canvas = new RenderGraphCanvas2D();
                    canvas.Begin();
                    for (int i = 0; i < 256; i++)
                    {
                        canvas.FillRect(new(.25f, .25f, 256.5f, .5f), PagingColor(i));
                    }

                    RenderGraphTexture handle = graph.ImportTexture(texture, TextureState.Undefined, default);
                    using var graphRenderer = new RenderGraphRenderer2D(renderer, canvas.End(), new() { Texture = handle, Description = description, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 0) }, new(graph));
                    Task<PreparedDrawingPasses2D> pending = graph.Append2D(graphRenderer).AsTask();
                    Pump(runtime, pending);
                    preparedGraph = pending.GetAwaiter().GetResult();
                    graph.ExportTexture(handle, TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead));
                    compiled = graph.Compile();
                    compiled.Record(commands);
                }
                else
                {
                    var canvas = new Canvas2D();
                    canvas.Begin();
                    for (int i = 0; i < 256; i++)
                    {
                        canvas.FillRect(new(.25f, .25f, 256.5f, .5f), PagingColor(i));
                    }

                    Task<PreparedDraw2D> pending = renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 0) }).AsTask();
                    Pump(runtime, pending);
                    direct = pending.GetAwaiter().GetResult();
                    commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
                    direct.Record(commands);
                    commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
                }

                commands.CopyTextureToBuffer(new() { Texture = texture, Width = 257, Height = 1 }, new() { Buffer = readback.SliceElements(0, 2304), BytesPerRow = 2304, RowsPerImage = 1 });
                commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, 2304), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
                commands.Finish();
                using IGraphicsSubmission submission = device.Queue.Submit([commands]);
                Pump(runtime, submission.WaitAsync().AsTask());
                commands.Dispose();
                Pump(runtime, readback.MapAsync().AsTask());
                byte[] pixels = new byte[2304];
                readback.CopyTo(pixels);
                readback.Unmap();
                return pixels.AsSpan(0, 257 * 8).ToArray();
            }
            catch (Exception primary)
            {
                Console.WriteLine($"PAGING PRIMARY: {primary}");
                throw;
            }
            finally
            {
                commands.Dispose();
                compiled?.Dispose();
                preparedGraph?.Dispose();
                direct?.Dispose();
            }
        }
        catch (Exception primary)
        {
            Console.WriteLine($"PAGING OUTER PRIMARY: {primary}");
            throw;
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static SRgba<float> PagingColor(int i) => i % 2 == 0 ? new(1, 0, 0, 1) : new(0, 0, 1, 1);

    private static DeviceCaps PagingCaps() => new()
    {
        MaxBufferSize = 1UL << 30,
        MaxStorageBufferBindingSize = 65536,
        MaxStorageBuffersPerStage = 7,
        MaxComputeInvocationsPerWorkgroup = 64,
        MaxComputeWorkgroupSizeX = 64,
        MaxComputeWorkgroupsPerDimension = 65535,
        ArgumentTableBindingLimits = new() { BufferSlots = 7, BuffersPerDraw = 6, MaximumBufferRangeBytes = 65536 },
    };

    private sealed class PagingCanonicalProducer : IStage<RenderRequest2D, Host<SceneV1>>, IStageFactory<RenderRequest2D, Host<SceneV1>>, IStageProcessor<RenderRequest2D, Host<SceneV1>>
    {
        public IStageFactory<RenderRequest2D, Host<SceneV1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(1024 * 1024, []);

        public IStageProcessor<RenderRequest2D, Host<SceneV1>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<SceneV1>>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> input, CancellationToken ct)
        {
            RenderRequest2D request = context.Read(input);
            Vector4[] cells = CpuNormalize2D.Normalize(request, new(2048, 65536, 262144, 1048576), ct);
            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<SceneV1, Vector4>(cells, new(cells.Length, request.Metadata()))));
        }

        public void Dispose()
        {
        }
    }

    private sealed class PagingCpuCoverage : IStage<RenderRequest2D, Host<Coverage16V1>>, IStageFactory<RenderRequest2D, Host<Coverage16V1>>, IStageProcessor<RenderRequest2D, Host<Coverage16V1>>
    {
        public IStageFactory<RenderRequest2D, Host<Coverage16V1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(32 * 1024 * 1024, []);

        public IStageProcessor<RenderRequest2D, Host<Coverage16V1>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<Coverage16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> input, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RenderRequest2D request = context.Read(input);
            float[] values = new float[257 * 256 * 16];
            for (int command = 0; command < 256; command++)
            {
                for (int x = 0; x < 257; x++)
                {
                    for (int sample = 0; sample < 16; sample++)
                    {
                        float px = x + (((sample % 4) + .5f) / 4), py = ((sample / 4) + .5f) / 4;
                        values[(((command * 257) + x) * 16) + sample] = px >= .25f && px < 256.75f && py >= .25f && py < .75f ? 1 : 0;
                    }
                }
            }

            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<Coverage16V1, float>(values, new(values.Length, request.Metadata()))));
        }

        public void Dispose()
        {
        }
    }
}
