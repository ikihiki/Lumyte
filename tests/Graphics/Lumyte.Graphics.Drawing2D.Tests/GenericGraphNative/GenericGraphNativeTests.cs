using System.Numerics;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Actual Vulkan structural graph pixels using a directly injected generic pipeline.</summary>
public sealed class GenericGraphNativeTests
{
    /// <summary>Checks every pixel twice with the same pipeline and immutable layered recording.</summary>
    /// <param name="scenario">Flat, finite layer, nested group opacity, or repeated identity backdrop.</param>
    /// <param name="cpuSparse">Whether coverage uses the CPU row-mask stage and explicit upload.</param>
    [GenericGpuNativeTheory]
    [Trait("Execution", "Gpu")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void DirectInjectedGraphPreservesPixelsAndReusesPipeline(int scenario, bool cpuSparse = false)
    {
        string backend = Environment.GetEnvironmentVariable("LUMYTE_GENERIC_GPU_BACKEND") ?? "Vulkan";
        if (backend is not ("Vulkan" or "Wgpu"))
        {
            throw new InvalidOperationException("Explicit native backend must be Vulkan or Wgpu.");
        }

        Type type = System.Reflection.Assembly.Load("Lumyte.Graphics." + backend).GetType("Lumyte.Graphics." + backend + "." + backend + "Device", true)!;
        System.Reflection.MethodInfo factory = type.GetMethods().Single(m => m.Name == "Create" && m.IsStatic && m.GetParameters().All(p => p.HasDefaultValue));
        var device = (IGraphicDevice)factory.Invoke(null, factory.GetParameters().Select(p => p.DefaultValue).ToArray())!;
        using var nativeOwner = (IDisposable)device;
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            var requests = new List<RenderRequest2D>();
            var pipelineGraph = new PipelineGraph<RenderRequest2D>();
            Port<Host<SceneV1>> hostScene = pipelineGraph.Stage(new CanonicalProducer(requests), pipelineGraph.Input);
            Port<Device<SceneV1>> scene = hostScene.Then(Transfers.Upload<SceneV1>());
            Port<Host<FrameV1>> hostFrame = pipelineGraph.Stage(RenderFields2D.Frame(), pipelineGraph.Input);
            Port<Device<FrameV1>> frame = hostFrame.Then(Transfers.Upload<FrameV1>());
            Port<Device<ResourceViewsV1>> views = pipelineGraph.Stage(RenderFields2D.ResourceViews(), pipelineGraph.Input);
            Port<Device<TargetV1>> target = pipelineGraph.Stage(RenderFields2D.Target(), pipelineGraph.Input);
            Port<Device<TargetWritesV1>> output;
            if (cpuSparse)
            {
                var samples = new SamplePolicy2D(16, 1);
                Port<Device<Coverage16V1>> coverage = pipelineGraph.Stage(new CpuSparseCoverage2D(samples, 256L * 1024 * 1024), hostScene, hostFrame).Then(Transfers.Upload<Coverage16V1>());
                Port<Device<Paint16V1>> paint = pipelineGraph.Stage(ComputeStages2D.Paint(samples), scene, views, frame);
                Port<Device<Clip16V1>> clip = pipelineGraph.Stage(ComputeStages2D.Clip(samples), scene, frame);
                output = pipelineGraph.Stage(RasterStages2D.Compose(samples), scene, coverage, paint, clip, target);
            }
            else
            {
                output = pipelineGraph.Stage(RasterStages2D.EvaluateAndComposite(new(16, 1)), scene, views, frame, target);
            }

            var policy = new GraphicsPipelinePolicy(512L * 1024 * 1024, 64L * 1024 * 1024, 128L * 1024 * 1024, 3);
            using var build = new GraphicsPipelineBuildHost(runtime, device, GenericGpuArtifactProvider2D.CreateEmbedded(), policy);
            using IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline = build.Build(pipelineGraph.Complete(output));
            using var renderer = new Renderer2D(device, pipeline, new(true, true, true, true, true, true, true, true, true, true, true), new(policy, DrawingArgumentTableMode.Auto, new()));
            RenderGraphDrawList2D list = Recording(scenario);
            byte[] first = Render(runtime, device, renderer, list, scenario, 0);
            byte[] second = Render(runtime, device, renderer, list, scenario, 1);
            Assert.Equal(first, second);
            Assert.Equal(scenario == 0 ? 2 : 4, requests.Count);
            if (scenario is 1 or 2)
            {
                Assert.Contains(requests, r => r.Target.PixelOrigin == new Vector2(1, 1) && r.Target.Description.Width == 2 && r.Target.Description.Height == 2);
            }

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bool inside = x is 1 or 2 && y is 1 or 2;
                    float blue = inside ? scenario switch { 1 => .5f, 2 => .25f, _ => 0 } : 0;
                    float green = scenario == 3 && x == 0 && y == 0 ? 1 : 0;
                    float[] expected = [1 - blue - green, green, blue, 1];
                    for (int c = 0; c < 4; c++)
                    {
                        float actual = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(first, (((y * 4) + x) * 8) + (c * 2)));
                        Assert.True(actual == expected[c], $"scenario={scenario} pixel=({x},{y}) channel={c}: expected {expected[c]}, actual {actual}");
                    }
                }
            }
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    /// <summary>Executes direct renderer download/resume/upload through the shared generic host without hidden preparation submission.</summary>
    /// <returns>The asynchronous execution qualification.</returns>
    [GenericGpuNativeFact]
    [Trait("Execution", "Gpu")]
    public async Task DirectRendererUsesExplicitSegmentedReadbackHostAsync()
    {
        Type type = System.Reflection.Assembly.Load("Lumyte.Graphics.Vulkan").GetType("Lumyte.Graphics.Vulkan.VulkanDevice", true)!;
        System.Reflection.MethodInfo factory = type.GetMethods().Single(m => m.Name == "Create" && m.IsStatic && m.GetParameters().All(p => p.HasDefaultValue));
        var device = (IGraphicDevice)factory.Invoke(null, factory.GetParameters().Select(p => p.DefaultValue).ToArray())!;
        using var nativeOwner = (IDisposable)device;
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            var requests = new List<RenderRequest2D>();
            var g = new PipelineGraph<RenderRequest2D>();
            Port<Device<SceneV1>> scene = g.Stage(new CanonicalProducer(requests), g.Input).Then(Transfers.Upload<SceneV1>()).Then(Transfers.Download<SceneV1>()).Then(Transfers.Upload<SceneV1>());
            Port<Device<FrameV1>> frame = g.Stage(RenderFields2D.Frame(), g.Input).Then(Transfers.Upload<FrameV1>());
            Port<Device<ResourceViewsV1>> views = g.Stage(RenderFields2D.ResourceViews(), g.Input);
            Port<Device<TargetV1>> targetPort = g.Stage(RenderFields2D.Target(), g.Input);
            var policy = new GraphicsPipelinePolicy(512L * 1024 * 1024, 64L * 1024 * 1024, 128L * 1024 * 1024, 3);
            using var build = new GraphicsPipelineBuildHost(runtime, device, GenericGpuArtifactProvider2D.CreateEmbedded(), policy);
            using IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline = build.Build(g.Complete(g.Stage(RasterStages2D.EvaluateAndComposite(new(16, 1)), scene, views, frame, targetPort)));
            using var renderer = new Renderer2D(device, pipeline, new(true, true, true, true, true, true, true, true, true, true, true), new(policy, DrawingArgumentTableMode.Auto, new()));
            using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView view = texture.CreateView();
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            using (IGraphicsCommandBuffer transition = device.CreateCommandBuffer(new()))
            {
                transition.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
                transition.Finish();
                using IGraphicsSubmission initial = device.Queue.Submit([transition]);
                Pump(runtime, initial.WaitAsync().AsTask());
            }

            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.FillRect(new(0, 0, 1, 1), new SRgba<float>(1, 0, 0, 1));
            DrawList2D list = canvas.End();
            var target = new RenderTarget2D { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 0) };
            Assert.Throws<NotSupportedException>(() => Pump(runtime, renderer.PrepareAsync(list, target).AsTask()));
            Task<PreparedPipelineExecution2D> pending = renderer.PrepareExecutionAsync(list, target).AsTask();
            Pump(runtime, pending);
            using PreparedPipelineExecution2D prepared = await pending;
            Assert.Single(requests);
            using var host = new GraphicsPipelineExecutionHost(pipeline, new InlineOwnerContext());
            Pump(runtime, host.ExecuteAsync(prepared.Execution).AsTask());
            prepared.Dispose();
            using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 256, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyTextureToBuffer(new() { Texture = texture, Width = 1, Height = 1 }, new() { Buffer = readback.SliceElements(0, 256), BytesPerRow = 256, RowsPerImage = 1 });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, 256), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            Pump(runtime, submission.WaitAsync().AsTask());
            commands.Dispose();
            Pump(runtime, readback.MapAsync().AsTask());
            byte[] pixels = new byte[256];
            readback.CopyTo(pixels);
            readback.Unmap();
            Assert.Equal(1f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 0)));
            Assert.Equal(0f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 2)));
            Assert.Equal(0f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 4)));
            Assert.Equal(1f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 6)));
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static RenderGraphDrawList2D Recording(int scenario)
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 4, 4), new SRgba<float>(1, 0, 0, 1));
        if (scenario is 1 or 2)
        {
            canvas.BeginLayer(new() { ContentBounds = new(1, 1, 2, 2), Opacity = .5f });
            if (scenario == 2)
            {
                canvas.BeginLayer(new() { ContentBounds = new(1, 1, 2, 2), Opacity = .5f });
            }

            canvas.FillRect(new(0, 0, 4, 4), new SRgba<float>(0, 0, 1, 1));
            canvas.EndLayer();
            if (scenario == 2)
            {
                canvas.EndLayer();
            }
        }
        else if (scenario == 3)
        {
            canvas.DrawBackdropEffect(new() { OutputBounds = new(1, 1, 2, 2), Effects = effects => effects.Source });
            canvas.FillRect(new(0, 0, 1, 1), new SRgba<float>(0, 1, 0, 1));
            canvas.DrawBackdropEffect(new() { OutputBounds = new(0, 0, 3, 3), Effects = effects => effects.Source });
        }

        return canvas.End();
    }

    private static byte[] Render(ExecutionRuntime runtime, IGraphicDevice device, Renderer2D renderer, RenderGraphDrawList2D list, int scenario, int frame)
    {
        using var graph = new Graph(device);
        var description = new TextureDesc { Width = 4, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource };
        using IGraphicsTexture texture = device.CreateTexture(description);
        RenderGraphTexture handle = graph.ImportTexture(texture, TextureState.Undefined, default);
        var target = new RenderTargetBinding2D { Texture = handle, Description = description, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 0) };
        using var graphRenderer = new RenderGraphRenderer2D(renderer, list, target, new(graph));
        Task<PreparedDrawingPasses2D> prepare = graph.Append2D(graphRenderer).AsTask();
        Pump(runtime, prepare);
        using PreparedDrawingPasses2D prepared = prepare.GetAwaiter().GetResult();
        graph.ExportTexture(handle, TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        using CompiledRenderGraph compiled = graph.Compile(frame, new RenderGraphCaptureOptions());
        Assert.NotNull(compiled.Snapshot);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 1024, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        compiled.Record(commands);
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = 4, Height = 4 }, new() { Buffer = readback.SliceElements(0, 1024), BytesPerRow = 256, RowsPerImage = 4 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, 1024), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        Pump(runtime, submission.WaitAsync().AsTask());
        commands.Dispose();
        Pump(runtime, readback.MapAsync().AsTask());
        byte[] padded = new byte[1024];
        readback.CopyTo(padded);
        readback.Unmap();
        byte[] pixels = Enumerable.Range(0, 4).SelectMany(y => padded.AsSpan(y * 256, 32).ToArray()).ToArray();
        string? directory = Environment.GetEnvironmentVariable("LUMYTE_GRAPH_NATIVE_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"scenario-{scenario}-frame-{frame}.rgba16f"), pixels);
            File.WriteAllLines(Path.Combine(directory, $"scenario-{scenario}-frame-{frame}.passes.txt"), compiled.PassNames);
            File.WriteAllText(Path.Combine(directory, $"scenario-{scenario}-frame-{frame}.snapshot.json"), System.Text.Json.JsonSerializer.Serialize(compiled.Snapshot));
        }

        return pixels;
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
                throw new TimeoutException("Native graph preparation/submission timed out.");
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private sealed class CanonicalProducer(List<RenderRequest2D> requests) : IStage<RenderRequest2D, Host<SceneV1>>, IStageFactory<RenderRequest2D, Host<SceneV1>>, IStageProcessor<RenderRequest2D, Host<SceneV1>>
    {
        public IStageFactory<RenderRequest2D, Host<SceneV1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(65536, []);

        public IStageProcessor<RenderRequest2D, Host<SceneV1>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<SceneV1>>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> a, CancellationToken ct)
        {
            RenderRequest2D request = context.Read(a);
            requests.Add(request);
            Vector4[] cells = CpuNormalize2D.Normalize(request, new(16, 64, 64, 64), ct);
            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<SceneV1, Vector4>(cells, new(cells.Length, request.Metadata()))));
        }

        public void Dispose()
        {
        }
    }

    private sealed class InlineOwnerContext : IGraphicsOwnerContext
    {
        public ValueTask InvokeAsync(Func<ValueTask> operation) => operation();
    }
}
