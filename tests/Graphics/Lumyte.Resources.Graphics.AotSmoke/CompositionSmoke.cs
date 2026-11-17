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
            Renderer.FillRect()[new SmokeRectangle(false), new SmokeRectangle(true)],
            Renderer.FillRoundedRect()[StandardFillRoundedRect()],
            Renderer.FillCircle()[StandardFillCircle()],
            Renderer.DrawLine()[StandardDrawLine()],
            Renderer.FillPath()[StandardFillPath()],
            Renderer.StrokePath()[StandardStrokePath()],
            Renderer.DrawImage()[StandardDrawImage()],
            Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
            Renderer.DrawDistanceField()[StandardDrawDistanceField()],
            Renderer.DrawMesh()[StandardDrawMesh()],
            Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]];
        var resources = new DrawingResources2D(device, pipeline);
        using RendererBuildHost2D host = definition.CreateHost();
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

    private sealed class SmokeRectangle(bool accepts) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(SmokeRectangleProcessor);

        protected override object CreateElement(PipelineBuildContext2D context) => new SmokeRectangleProcessor(accepts);
    }

    private sealed class SmokeRectangleProcessor(bool accepts) : PipelineElement2D, IFillRectRenderer2D, IPipelineAdmission2D<FillRectCommand2D>
    {
        private readonly IFillRectRenderer2D _standard = new DefaultFillRectRenderer2D();

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands) => accepts;

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default) => _standard.PrepareAsync(context, commands, cancellationToken);
    }
}
