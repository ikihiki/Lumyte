using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

IGraphicDevice device = args.Contains("wgpu") ? WgpuDevice.Create() : VulkanDevice.Create();
using var deviceOwner = (IDisposable)device;
var canvas = new Canvas2D();
canvas.Begin();
var path = new PathBuilder2D();
path.MoveTo(new(0, 0));
path.QuadraticTo(new(4, 0), new(4, 4));
path.LineTo(new(0, 4));
path.Close();
canvas.FillPath(path.Build(), PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1)));
DrawList2D content = canvas.End();
foreach (IRenderer2D renderer in new IRenderer2D[] { Renderer2D.CreateDefault(device), new SparseStripRenderer2D(device), new ComputeRenderer2D(device) })
{
    using (renderer)
    using (var graph = new Graph(device, "strict-aot-renderer"))
    {
        EffectImage image = await graph.Append2D(renderer, content, new DrawingImageOptions2D { Bounds = new(0, 0, 8, 8) });
        EffectImage output = graph.AppendOpacity(image, 0.5f);
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba16Float);
        uint pitch = ((64 + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment;
        uint count = pitch * 8;
        RenderGraphBuffer<byte> readback = graph.CreateBuffer<byte>(new() { Count = count, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "readback");
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        graph.AddPass("readback", context => context.Commands.CopyTextureToBuffer(
            new() { Texture = context.GetTexture(output.Texture), Width = 8, Height = 8 },
            new() { Buffer = context.GetBuffer(readback).SliceElements(0, count), BytesPerRow = pitch, RowsPerImage = 8 }))
            .UseTexture(output.Texture, TextureState.CopySource, copyRead).UseBuffer(readback, copyWrite);
        graph.ExportBuffer(readback, new(PipelineStage.Host, ResourceAccess.HostRead));
        using CompiledRenderGraph plan = graph.Compile();
        renderer.Dispose();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        IGraphicsBuffer<byte> buffer = plan.GetBuffer(readback);
        await buffer.MapAsync();
        byte[] bytes = new byte[count];
        buffer.CopyTo(bytes);
        buffer.Unmap();
        float red = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, (int)pitch + 8));
        float alpha = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, (int)pitch + 14));
        if (MathF.Abs(red - 0.5f) > 0.001f || MathF.Abs(alpha - 0.5f) > 0.001f)
        {
            throw new InvalidOperationException($"AOT {renderer.GetType().Name} pixel: {red}, {alpha}");
        }

        Console.WriteLine($"AOT {renderer.GetType().Name}: curve/paint/graph/ImageEffects/readback passed");
    }
}
