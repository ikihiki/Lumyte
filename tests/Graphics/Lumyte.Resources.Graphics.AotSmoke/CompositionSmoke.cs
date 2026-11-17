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
            Renderer.FillRect()[BackendBinding2D<IFillRectRenderer2D>.Borrowed(new DefaultFillRectRenderer2D())],
            Renderer.FillRoundedRect()[BackendBinding2D<IFillRoundedRectRenderer2D>.Borrowed(new DefaultFillRoundedRectRenderer2D())],
            Renderer.FillCircle()[BackendBinding2D<IFillCircleRenderer2D>.Borrowed(new DefaultFillCircleRenderer2D())],
            Renderer.DrawLine()[BackendBinding2D<IDrawLineRenderer2D>.Borrowed(new DefaultDrawLineRenderer2D())],
            Renderer.FillPath()[BackendBinding2D<IFillPathRenderer2D>.Borrowed(new DefaultFillPathRenderer2D())],
            Renderer.StrokePath()[BackendBinding2D<IStrokePathRenderer2D>.Borrowed(new DefaultStrokePathRenderer2D())],
            Renderer.DrawImage()[BackendBinding2D<IDrawImageRenderer2D>.Borrowed(new DefaultDrawImageRenderer2D())],
            Renderer.DrawCoverageMask()[BackendBinding2D<IDrawCoverageMaskRenderer2D>.Borrowed(new DefaultDrawCoverageMaskRenderer2D())],
            Renderer.DrawDistanceField()[BackendBinding2D<IDrawDistanceFieldRenderer2D>.Borrowed(new DefaultDrawDistanceFieldRenderer2D())],
            Renderer.DrawMesh()[BackendBinding2D<IDrawMeshRenderer2D>.Borrowed(new DefaultDrawMeshRenderer2D())],
            Renderer.DrawPaintGraph()[BackendBinding2D<IDrawPaintGraphRenderer2D>.Borrowed(new DefaultDrawPaintGraphRenderer2D())]];
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
