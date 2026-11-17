using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class CompositionSmoke
{
    internal static async Task RunAsync()
    {
        using var device = WgpuDevice.Create();
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        Definitions.Renderer definition = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D { MaxFramesInFlight = 2, MaxCachedAssets = 8, MaxCachedBytes = 1024 * 1024 })[
            FillRect(BackendBinding2D<IFillRectRenderer2D>.Borrowed(new DefaultFillRectRenderer2D())),
            FillRoundedRect(BackendBinding2D<IFillRoundedRectRenderer2D>.Borrowed(new DefaultFillRoundedRectRenderer2D())),
            FillCircle(BackendBinding2D<IFillCircleRenderer2D>.Borrowed(new DefaultFillCircleRenderer2D())),
            DrawLine(BackendBinding2D<IDrawLineRenderer2D>.Borrowed(new DefaultDrawLineRenderer2D())),
            FillPath(BackendBinding2D<IFillPathRenderer2D>.Borrowed(new DefaultFillPathRenderer2D())),
            StrokePath(BackendBinding2D<IStrokePathRenderer2D>.Borrowed(new DefaultStrokePathRenderer2D())),
            DrawImage(BackendBinding2D<IDrawImageRenderer2D>.Borrowed(new DefaultDrawImageRenderer2D())),
            DrawCoverageMask(BackendBinding2D<IDrawCoverageMaskRenderer2D>.Borrowed(new DefaultDrawCoverageMaskRenderer2D())),
            DrawDistanceField(BackendBinding2D<IDrawDistanceFieldRenderer2D>.Borrowed(new DefaultDrawDistanceFieldRenderer2D())),
            DrawMesh(BackendBinding2D<IDrawMeshRenderer2D>.Borrowed(new DefaultDrawMeshRenderer2D())),
            DrawPaintGraph(BackendBinding2D<IDrawPaintGraphRenderer2D>.Borrowed(new DefaultDrawPaintGraphRenderer2D()))];
        var resources = new DrawingResources2D(device, pipeline);
        if (definition.DescribeDataflow(device, resources).Count != 11)
        {
            throw new InvalidOperationException("Missing compiled kind routes.");
        }

        using IRenderer2D renderer = definition.BuildRenderer(device, resources, null, null);
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 16, 16), new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new(8, 8), 4, new SRgba<float>(0, 1, 0, 0.5f));
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
