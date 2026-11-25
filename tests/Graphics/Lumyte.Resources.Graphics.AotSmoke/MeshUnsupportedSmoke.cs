using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class MeshUnsupportedSmoke
{
    internal static async Task RunAsync()
    {
        using var device = WgpuDevice.Create();
        RejectGenericMesh(device);
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using RendererBuildHost2D host = Configuration().CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, new(device, pipeline), null, null);
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 8, 8), new SRgba<float>(1, 0, 0, 1));
        try
        {
            using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        }
        catch (NotSupportedException error) when (error.Message == "Every candidate for the selected command kind rejected the batch.")
        {
            Console.WriteLine("Mesh unsupported: Wgpu rejected the Mesh-only candidate before shader/pipeline allocation or submission.");
            return;
        }

        throw new InvalidOperationException("Wgpu unexpectedly admitted a Mesh-only candidate.");
    }

    private static void RejectGenericMesh(IGraphicDevice device)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new());
        var graph = new PipelineGraph<RenderRequest2D>();
        Port<Host<CommandsV1>> commands = graph.Stage(RenderFields2D.Commands(), graph.Input);
        Port<Host<ResourceManifestV1>> manifest = graph.Stage(RenderFields2D.ResourceManifest(), graph.Input);
        Port<Host<FrameV1>> frame = graph.Stage(RenderFields2D.Frame(), graph.Input);
        Port<Device<SceneV1>> scene = graph.Stage(CpuStages2D.Normalize(new(16, 64, 64, 64)), commands, manifest, frame).Then(Transfers.Upload<SceneV1>());
        Port<Device<FrameV1>> deviceFrame = frame.Then(Transfers.Upload<FrameV1>());
        Port<Device<ResourceViewsV1>> views = graph.Stage(RenderFields2D.ResourceViews(), graph.Input);
        Port<Device<TargetV1>> target = graph.Stage(RenderFields2D.Target(), graph.Input);
        Port<Device<TargetWritesV1>> output = graph.Stage(MeshStages2D.EvaluateAndComposite(new(16, 1)), scene, views, deviceFrame, target);
        using var build = new GraphicsPipelineBuildHost(runtime, device, GenericGpuArtifactProvider2D.CreateEmbedded(), new(64 * 1024 * 1024, 16 * 1024 * 1024, 16 * 1024 * 1024, 3));
        try
        {
            using IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline = build.Build(graph.Complete(output));
        }
        catch (NotSupportedException)
        {
            Console.WriteLine("Native AOT generic Mesh: unsupported Wgpu capability rejected at build; no fallback or submission.");
            return;
        }

        throw new InvalidOperationException("Wgpu unexpectedly built a generic Mesh pipeline.");
    }

    private static Definitions.Renderer Configuration(bool resident = false) => Renderer(
        argumentTables: DrawingArgumentTableMode.Auto,
        retention: resident ? RetentionGpuLimits(3, 32, 4 * 1024 * 1024, 16 * 1024 * 1024) : RetentionLimits(maxFramesInFlight: 3, maxCachedAssets: 32, maxCachedBytes: 4 * 1024 * 1024))[
        Renderer.FillRect()[MeshFillRect(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.FillRoundedRect()[MeshFillRoundedRect(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.FillCircle()[MeshFillCircle(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawLine()[MeshDrawLine(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.FillPath()[MeshFillPath(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.StrokePath()[MeshStrokePath(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawImage()[MeshDrawImage(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawCoverageMask()[MeshDrawCoverageMask(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawDistanceField()[MeshDrawDistanceField(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawMesh()[StandardDrawMesh()],
        Renderer.DrawPaintGraph()[MeshDrawPaintGraph(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))]];
}
