using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class SparseSmoke
{
    internal static async Task RunAsync()
    {
        using var device = WgpuDevice.Create();
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(2, 8, 1024 * 1024, 256 * 1024 * 1024))[
            Renderer.FillRect()[SparseFillRect(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.FillRoundedRect()[SparseFillRoundedRect(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.FillCircle()[SparseFillCircle(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.DrawLine()[SparseDrawLine(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.FillPath()[SparseFillPath(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.StrokePath()[SparseStrokePath(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.DrawImage()[SparseDrawImage(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.DrawCoverageMask()[SparseDrawCoverageMask(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.DrawDistanceField()[SparseDrawDistanceField(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.DrawMesh()[SparseDrawMesh(CpuSparse(1024 * 1024, 8, 65536, 262144))],
            Renderer.DrawPaintGraph()[SparseDrawPaintGraph(CpuSparse(1024 * 1024, 8, 65536, 262144))]].CreateHost();
        var resources = new DrawingResources2D(device, pipeline);
        if (host.DescribeDataflow(device, resources).Count != 11)
        {
            throw new InvalidOperationException("Missing compiled kind routes.");
        }

        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
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
        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba16Float).BytesPerRowAlignment;
        uint pitch = ((128 + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 16, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = 16, Height = 16 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 16 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] bytes = new byte[readback.Count];
        readback.CopyTo(bytes);
        readback.Unmap();
        int pixel = checked((int)((15 * pitch) + (15 * 8)));
        if (BitConverter.ToHalf(bytes, pixel) != (Half)1 || BitConverter.ToHalf(bytes, pixel + 2) != (Half)0 || BitConverter.ToHalf(bytes, pixel + 4) != (Half)0 || BitConverter.ToHalf(bytes, pixel + 6) != (Half)1)
        {
            throw new InvalidOperationException("Actual sparse mode3 GPU paint pixel mismatch.");
        }

        commands.Dispose();
        prepared.Dispose();
        Console.WriteLine("Native AOT genuine Sparse factories, mode3 GPU paint and actual native wgpu pixel passed.");
    }
}
