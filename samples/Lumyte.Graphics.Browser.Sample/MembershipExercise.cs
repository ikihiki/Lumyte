using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Verification;
using Lumyte.Mathematics.Colors;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Browser.Sample;

internal static class MembershipExercise
{
    internal static async Task RunAsync(IGraphicDevice raw)
    {
        IGraphicDevice device = new MembershipMultipageExercise.RangeDevice(raw);
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionLimits(maxFramesInFlight: 2, maxCachedAssets: 8, maxCachedBytes: 1024 * 1024))[
            Renderer.FillRect()[CoverageMembershipFillRect()],
            Renderer.FillRoundedRect()[CoverageMembershipFillRoundedRect()],
            Renderer.FillCircle()[CoverageMembershipFillCircle()],
            Renderer.DrawLine()[CoverageMembershipDrawLine()],
            Renderer.FillPath()[CoverageMembershipFillPath()],
            Renderer.StrokePath()[CoverageMembershipStrokePath()],
            Renderer.DrawImage()[CoverageMembershipDrawImage()],
            Renderer.DrawCoverageMask()[CoverageMembershipDrawCoverageMask()],
            Renderer.DrawDistanceField()[CoverageMembershipDrawDistanceField()],
            Renderer.DrawMesh()[CoverageMembershipDrawMesh()],
            Renderer.DrawPaintGraph()[CoverageMembershipDrawPaintGraph()]].CreateHost();
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
        CheckPixel(bytes, checked((int)((pitch * 14) + (14 * 8))), 1, 0, 0, 1);
        CheckPixel(bytes, checked((int)((pitch * 4) + (6 * 8))), 0, 0, 1, 1);
        commands.Dispose();
        prepared.Dispose();
        await MembershipMultipageExercise.RunAsync(device, renderer);
        Console.WriteLine("Browser membership eleven-kind factories, path/stroke compute and strict WebGPU pixel checks passed.");
    }

    private static void CheckPixel(byte[] bytes, int offset, float r, float g, float b, float a)
    {
        float[] expected = [r, g, b, a];
        for (int channel = 0; channel < 4; channel++)
        {
            float actual = (float)BitConverter.UInt16BitsToHalf(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + (channel * 2), 2)));
            if (actual != expected[channel])
            {
                throw new InvalidOperationException("Membership browser pixel differs from the exact primary-color oracle.");
            }
        }
    }
}
