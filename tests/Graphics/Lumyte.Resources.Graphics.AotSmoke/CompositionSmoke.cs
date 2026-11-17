using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;
using static Lumyte.Resources.Graphics.AotSmoke.CustomCompose;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class CompositionSmoke
{
    internal static async Task RunAsync()
    {
        using var device = WgpuDevice.Create();
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionLimits(maxFramesInFlight: 2, maxCachedAssets: 8, maxCachedBytes: 1024 * 1024))[
            Renderer.FillRect()[FastRectangles(accepts: false), FastRectangles(accepts: true)],
            Renderer.FillRoundedRect()[StandardFillRoundedRect()],
            Renderer.FillCircle()[StandardFillCircle()],
            Renderer.DrawLine()[StandardDrawLine()],
            Renderer.FillPath()[Paths(maxSegments: 4096)],
            Renderer.StrokePath()[Paths(maxSegments: 4096)],
            Renderer.DrawImage()[StandardDrawImage()],
            Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
            Renderer.DrawDistanceField()[StandardDrawDistanceField()],
            Renderer.DrawMesh()[StandardDrawMesh()],
            Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]].CreateHost();
        var resources = new DrawingResources2D(device, pipeline);
        if (host.DescribeDataflow(device, resources).Count != 11)
        {
            throw new InvalidOperationException("Missing compiled kind routes.");
        }

        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 16, 16), new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new(8, 8), 4, new SRgba<float>(0, 1, 0, 0.5f));
        var path = new PathBuilder2D();
        path.MoveTo(new(1, 1));
        path.LineTo(new(12, 1));
        path.LineTo(new(6, 12));
        path.Close();
        Path2D triangle = path.Build();
        canvas.FillPath(triangle, PaintGraph2D.Solid(new SRgba<float>(0, 0, 1, 1)));
        canvas.StrokePath(triangle, PaintGraph2D.Solid(new SRgba<float>(1, 1, 0, 1)), new StrokeStyle2D());
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite) });
        prepared.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        commands.Dispose();
        prepared.Dispose();
        Console.WriteLine("Native AOT eleven-kind composed renderer native wgpu submission completed.");
    }
}
