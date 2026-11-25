using System.Numerics;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Opt-in actual Vulkan dispatch/readback checks; no fake GPU is substituted.</summary>
public sealed partial class GenericGpuNativeTests
{
    /// <summary>Preserves each fractional rectangle sample in command-major native storage.</summary>
    [GenericGpuNativeFact]
    [Trait("Execution", "Gpu")]
    public void CoverageDispatchPreservesSixteenCorrelatedSamples()
    {
        var state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        var paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));
        RenderRequest2D request = Request([new FillRectCommand2D(new(.25f, .25f, .5f, .5f), paint, state)]);
        float[] values = Run(request, ComputeStages2D.Coverage(new(16, 1)));
        Assert.Equal(16, values.Length);
        for (int s = 0; s < 16; s++)
        {
            Assert.Equal(s % 4 is 1 or 2 && s / 4 is 1 or 2 ? 1 : 0, values[s]);
        }
    }

    /// <summary>Broadcasts inverse-pixel-center nonbinary mesh coverage rather than reinterpolating samples.</summary>
    [GenericGpuNativeFact]
    [Trait("Execution", "Gpu")]
    public void MeshCenterCoverageIsBroadcastBeforePaintAndClip()
    {
        var state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        var paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));
        var mesh = new Mesh2D([new(new(0, 0), .25f), new(new(2, 0), .5f), new(new(0, 2), .75f)], [0, 1, 2]);
        float[] values = Run(Request([new DrawMeshCommand2D(mesh, paint, state)]), ComputeStages2D.Coverage(new(16, 1)));
        Assert.All(values, value => Assert.Equal(.4375f, value, 6));
    }

    /// <summary>Expands CPU-normalized analytic clip sample bounds without GPU geometric predicates.</summary>
    [GenericGpuNativeFact]
    [Trait("Execution", "Gpu")]
    public void ClipDispatchUsesCpuNormalizedSampleMask()
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(.25f, .25f));
        path.LineTo(new(.75f, .25f));
        path.LineTo(new(.25f, .75f));
        path.Close();
        var clips = new ClipStack2D([new(path.Build(), Matrix3x2.Identity, FillRule2D.NonZero)]);
        var state = new DrawingState2D(Matrix3x2.Identity, clips);
        var paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));
        RenderRequest2D request = Request([new FillRectCommand2D(new(0, 0, 1, 1), paint, state)]);
        float[] values = Run(request, ComputeStages2D.Clip(new(16, 1)));
        Vector4[] cells = CpuNormalize2D.Normalize(request, new(16, 64, 64, 64), default);
        uint kind = BitConverter.SingleToUInt32Bits(cells[3].X);
        Assert.Equal((uint)NormalizedClipKind2D.SampleMask, kind);
        uint bits = (uint)cells[(int)BitConverter.SingleToUInt32Bits(cells[3].Y)].X;
        for (int s = 0; s < 16; s++)
        {
            Assert.Equal((bits >> s) & 1, values[s]);
        }
    }

    /// <summary>Executes independent and fused stages and checks command-average SrcOver against an analytic pixel.</summary>
    [GenericGpuNativeFact]
    [Trait("Execution", "Gpu")]
    public void SeparatedAndFusedPixelsPreserveCommandAveragePainterOrder()
    {
        byte[] separated = Render(false, 0);
        byte[] fused = Render(true, 0);
        Assert.Equal(separated, fused);

        // Two identical quarter-covered opaque commands: qRed=.25, then qBlue=.25.
        // Average-before-SrcOver gives red=.1875, blue=.25, alpha=.4375.
        // Compositing per sample instead would incorrectly erase all red.
        Assert.Equal(.1875f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(separated, 0)));
        Assert.Equal(0f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(separated, 2)));
        Assert.Equal(.25f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(separated, 4)));
        Assert.Equal(.4375f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(separated, 6)));
    }

    /// <summary>Preserves mixed command ordering and nonlinear shape evaluation across decomposed and fused shaders.</summary>
    [GenericGpuNativeFact]
    [Trait("Execution", "Gpu")]
    public void SeparatedAndFusedMixedShapePixelsAgree()
    {
        byte[] separated = Render(false, 1);
        byte[] fused = Render(true, 1);
        Assert.Equal(separated, fused);
        Assert.True((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(separated, 0)) > 0);
        Assert.True((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(separated, 2)) > 0);
    }

    /// <summary>Rejects mismatched scene/frame identity before any processor can allocate output.</summary>
    [Fact]
    public void DescribeRejectsDifferentSourceBeforeNativeAllocation()
    {
        RenderRequest2D a = Request([]);
        RenderRequest2D b = Request([]);
        var descriptions = (StageInputDescriptions)Activator.CreateInstance(typeof(StageInputDescriptions), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { new[] { a.Extent(1), b.Extent(2) }, long.MaxValue }, null)!;
        Assert.Throws<InvalidOperationException>(() => ComputeStages2D.Coverage(new(16, 1)).Factory.Describe(descriptions));
    }

    /// <summary>Changes only coverage producer and explicit upload while preserving downstream stages.</summary>
    /// <param name="scenario">The unchanged native canonical corpus scenario.</param>
    [GenericGpuNativeTheory]
    [Trait("Execution", "Gpu")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public void CpuSparseCoverageSubstitutesComputeWithSameDownstreamPixels(int scenario)
        => Assert.Equal(Render(false, scenario), Render(false, scenario, cpuSparse: true));

    /// <summary>Checks inclusive rounded, half-open rectangle and strict circle pixels on both coverage producers.</summary>
    [GenericGpuNativeFact]
    [Trait("Execution", "Gpu")]
    public void CpuAndGpuCanonicalBoundaryPixelsKeepNineFourOneSamples()
    {
        foreach (bool cpu in new[] { false, true })
        {
            byte[] pixels = Render(false, 24, cpuSparse: cpu);
            float[] expected = [9f / 16, 4f / 16, 1f / 16];
            for (int x = 0; x < 3; x++)
            {
                Assert.Equal(expected[x], (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, (x * 8) + 6)));
            }
        }
    }

    private static byte[] Render(bool fused, int scenario, bool mesh = false, bool cpuSparse = false)
    {
        uint width = scenario == 24 ? 3u : scenario == 23 ? 2u : 1u;
        IGraphicDevice device = CreateDevice();
        using var nativeOwner = (IDisposable)device;
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            var graph = new PipelineGraph<RenderRequest2D>();
            Port<Host<SceneV1>> hostScene = graph.Stage(new CanonicalProducer(), graph.Input);
            Port<Device<SceneV1>> scene = hostScene.Then(Transfers.Upload<SceneV1>());
            Port<Host<FrameV1>> hostFrame = graph.Stage(RenderFields2D.Frame(), graph.Input);
            Port<Device<FrameV1>> frame = hostFrame.Then(Transfers.Upload<FrameV1>());
            Port<Device<ResourceViewsV1>> views = graph.Stage(RenderFields2D.ResourceViews(), graph.Input);
            Port<Device<TargetV1>> target = graph.Stage(RenderFields2D.Target(), graph.Input);
            var samples = new SamplePolicy2D(16, 1);
            Port<Device<TargetWritesV1>> output;
            if (mesh)
            {
                output = graph.Stage(MeshStages2D.EvaluateAndComposite(samples), scene, views, frame, target);
            }
            else if (fused)
            {
                output = graph.Stage(RasterStages2D.EvaluateAndComposite(samples), scene, views, frame, target);
            }
            else
            {
                Port<Device<Coverage16V1>> coverage = cpuSparse
                    ? graph.Stage(new CpuSparseCoverage2D(samples, 256L * 1024 * 1024), hostScene, hostFrame).Then(Transfers.Upload<Coverage16V1>())
                    : graph.Stage(ComputeStages2D.PagedCoverage(samples, new GenericPagingLimits2D()), scene, frame);
                Port<Device<Paint16V1>> paint = graph.Stage(ComputeStages2D.PagedPaint(samples, new GenericPagingLimits2D()), scene, views, frame);
                Port<Device<Clip16V1>> clip = graph.Stage(ComputeStages2D.PagedClip(samples, new GenericPagingLimits2D()), scene, frame);
                output = graph.Stage(RasterStages2D.PagedCompose(samples, new GenericPagingLimits2D()), scene, coverage, paint, clip, target);
            }

            var policy = new GraphicsPipelinePolicy(512L * 1024 * 1024, 64L * 1024 * 1024, 128L * 1024 * 1024, 3);
            using var build = new GraphicsPipelineBuildHost(runtime, device, Artifacts(), policy);
            using IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline = build.Build(graph.Complete(output));
            using var renderer = new Renderer2D(device, pipeline, new(true, true, true, true, true, true, true, true, true, true, true), new(policy, DrawingArgumentTableMode.Auto, new()));
            using IGraphicsTexture texture = device.CreateTexture(new() { Width = width, Height = 1, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView view = texture.CreateView();
            var canvas = new Canvas2D();
            canvas.Begin();
            if (scenario == 0)
            {
                canvas.FillRect(new(.25f, .25f, .5f, .5f), new SRgba<float>(1, 0, 0, 1));
                canvas.FillRect(new(.25f, .25f, .5f, .5f), new SRgba<float>(0, 0, 1, 1));
            }
            else if (scenario == 1)
            {
                canvas.FillCircle(new(.5f, .5f), .4f, new SRgba<float>(1, 0, 0, 1));
                canvas.FillRoundedRect(new(.1f, .1f, .8f, .8f), .2f, new SRgba<float>(0, 1, 0, .5f));
            }

            using var resources = new NativeCorpusResources(device, runtime);
            if (scenario == 24)
            {
                canvas.FillRoundedRect(new(.125f, .125f, .5f, .5f), 0, new SRgba<float>(1, 0, 0, 1));
                canvas.FillRect(new(1.125f, .125f, .5f, .5f), new SRgba<float>(1, 0, 0, 1));
                canvas.FillCircle(new(2.375f, .375f), .25f, new SRgba<float>(1, 0, 0, 1));
            }
            else if (scenario == 23)
            {
                canvas.ClipRect(new(0, 0, 1, 1));
                canvas.FillRect(new(0, 0, 2, 1), new SRgba<float>(1, 0, 0, 1));
            }
            else if (scenario >= 2)
            {
                PopulateCorpus(canvas, resources, scenario);
            }

            Task<PreparedDraw2D> prepare = renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 0) }).AsTask();
            Pump(runtime, prepare);
            using PreparedDraw2D prepared = prepare.GetAwaiter().GetResult();
            using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 256, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            prepared.Record(commands);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyTextureToBuffer(new() { Texture = texture, Width = width, Height = 1 }, new() { Buffer = readback.SliceElements(0, 256), BytesPerRow = 256, RowsPerImage = 1 });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, 256), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            Pump(runtime, submission.WaitAsync().AsTask());
            commands.Dispose();
            Pump(runtime, readback.MapAsync().AsTask());
            byte[] pixels = new byte[256];
            readback.CopyTo(pixels);
            readback.Unmap();
            byte[] result = pixels.AsSpan(0, checked((int)width * 8)).ToArray();
            string? evidence = Environment.GetEnvironmentVariable("LUMYTE_GENERIC_GPU_PIXEL_EVIDENCE");
            if (evidence is not null)
            {
                Directory.CreateDirectory(evidence);
                File.WriteAllBytes(Path.Combine(evidence, $"scenario-{scenario}-{(mesh ? "mesh" : fused ? "fused" : "separated")}.rgba16f"), result);
                File.WriteAllText(Path.Combine(evidence, "device-caps.json"), System.Text.Json.JsonSerializer.Serialize(device.Caps));
            }

            return result;
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static IGraphicDevice CreateDevice()
    {
        string backend = Environment.GetEnvironmentVariable("LUMYTE_GENERIC_GPU_BACKEND") ?? "Vulkan";
        if (backend is not ("Vulkan" or "Wgpu"))
        {
            throw new InvalidOperationException("Explicit native backend must be Vulkan or Wgpu.");
        }

        Type type = System.Reflection.Assembly.Load("Lumyte.Graphics." + backend).GetType("Lumyte.Graphics." + backend + "." + backend + "Device", true)!;
        System.Reflection.MethodInfo factory = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Single(method => method.Name == "Create" && method.GetParameters().All(parameter => parameter.HasDefaultValue));
        return (IGraphicDevice)factory.Invoke(null, factory.GetParameters().Select(parameter => parameter.DefaultValue).ToArray())!;
    }

    private static RenderRequest2D Request(DrawingCommand2D[] commands)
        => new(new DrawingTargetInfo2D { Description = new TextureDesc { Width = 1, Height = 1, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } }, commands, Guid.NewGuid());

    private static float[] Run<TSchema>(RenderRequest2D request, IStage<Device<SceneV1>, Device<FrameV1>, Device<TSchema>> stage)
        where TSchema : ISchema
    {
        IGraphicDevice device = CreateDevice();
        using var nativeOwner = (IDisposable)device;
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            var graph = new PipelineGraph<RenderRequest2D>();
            Port<Device<SceneV1>> scene = graph.Stage(new CanonicalProducer(), graph.Input).Then(Transfers.Upload<SceneV1>());
            Port<Device<FrameV1>> frame = graph.Stage(RenderFields2D.Frame(), graph.Input).Then(Transfers.Upload<FrameV1>());
            Port<float[]> output = graph.Stage(stage, scene, frame).Then(Transfers.Download<TSchema>()).Then(new ReadFloats<TSchema>());
            using var build = new GraphicsPipelineBuildHost(runtime, device, Artifacts(), new(512L * 1024 * 1024, 64L * 1024 * 1024, 128L * 1024 * 1024, 3));
            using IPipeline<RenderRequest2D, float[]> pipeline = build.Build(graph.Complete(output));
            Task<PipelineResult<float[]>> prepare = pipeline.PrepareAsync(request).AsTask();
            Pump(runtime, prepare);
            using PipelineResult<float[]> result = prepare.GetAwaiter().GetResult();
            var context = new InlineContext();
            using GraphicsPipelineExecution execution = result.AcquireExecution(pipeline, context);
            using var host = new GraphicsPipelineExecutionHost(pipeline, context);
            try
            {
                Pump(runtime, host.ExecuteAsync(execution).AsTask());
            }
            finally
            {
                Pump(runtime, host.DrainAsync().AsTask());
            }

            return result.Value;
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static GenericGpuArtifactProvider2D Artifacts()
    {
        string? path = Environment.GetEnvironmentVariable("LUMYTE_GENERIC_GPU_ARTIFACTS");
        if (path is null)
        {
            return GenericGpuArtifactProvider2D.CreateEmbedded();
        }

        return new(Directory.EnumerateFiles(path, "generic-*.lshader").ToDictionary(file => Path.GetFileNameWithoutExtension(file)!, file =>
        {
            using Stream stream = File.OpenRead(file);
            return ShaderArtifact.Load(stream);
        })!);
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
                throw new TimeoutException("Native generic GPU stage gate timed out.");
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private sealed class CanonicalProducer : IStage<RenderRequest2D, Host<SceneV1>>, IStageFactory<RenderRequest2D, Host<SceneV1>>, IStageProcessor<RenderRequest2D, Host<SceneV1>>
    {
        public IStageFactory<RenderRequest2D, Host<SceneV1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(65536, []);

        public IStageProcessor<RenderRequest2D, Host<SceneV1>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<SceneV1>>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> a, CancellationToken ct)
        {
            RenderRequest2D request = context.Read(a);
            Vector4[] cells = CpuNormalize2D.Normalize(request, new(16, 64, 64, 64), ct);
            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<SceneV1, Vector4>(cells, new(cells.Length, request.Metadata()))));
        }

        public void Dispose()
        {
        }
    }

    private sealed class ReadFloats<TSchema> : IStage<Host<TSchema>, float[]>, IStageFactory<Host<TSchema>, float[]>, IStageProcessor<Host<TSchema>, float[]>
        where TSchema : ISchema
    {
        public IStageFactory<Host<TSchema>, float[]> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(65536, []);

        public IStageProcessor<Host<TSchema>, float[]> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<float[]>> PrepareAsync(StagePreparationContext context, ValueLease<Host<TSchema>> a, CancellationToken ct)
            => ValueTask.FromResult(context.Publish(context.ReadHost(a).ReadElements<float>().ToArray()));

        public void Dispose()
        {
        }
    }

    private sealed class InlineContext : IGraphicsOwnerContext
    {
        public ValueTask InvokeAsync(Func<ValueTask> operation) => operation();
    }
}
