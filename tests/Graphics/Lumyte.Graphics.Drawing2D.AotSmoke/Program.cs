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

foreach (bool standard in new[] { false, true })
{
    using Renderer2D renderer = standard ? Renderer2D.CreateDefault(device, retentionOptions: new() { EnableResidentInstances = true }) : new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
    using var scene = new DrawingScene2D();
    var residentCanvas = new Canvas2D();
    residentCanvas.Begin();
    residentCanvas.FillRect(new(2, 2, 2, 2), new SRgba<float>(1, 0, 0, 1));
    DrawingNodeId2D node = scene.Insert(scene.Root, 0, residentCanvas.End());
    for (int version = 0; version < 2; version++)
    {
        scene.SetTransform(node, System.Numerics.Matrix3x2.CreateTranslation(version, version));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        var description = new TextureDesc { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource };
        using IGraphicsTexture texture = device.CreateTexture(description);
        using IGraphicsTextureView view = texture.CreateView();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        using var graph = new Graph(device, "strict-aot-resident");
        RenderGraphTexture target = graph.ImportTexture(texture, TextureState.Undefined, default);
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(texture, target);
        graph.AppendResident2D(frame, new() { Texture = target, Description = description, PixelScale = 1 }, bindings);
        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba16Float).BytesPerRowAlignment;
        uint pitch = ((64 + alignment - 1) / alignment) * alignment;
        RenderGraphBuffer<byte> readback = graph.CreateBuffer<byte>(new() { Count = pitch * 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        graph.AddPass("resident.readback", context => context.Commands.CopyTextureToBuffer(new() { Texture = texture, Width = 8, Height = 8 }, new() { Buffer = context.GetBuffer(readback).SliceElements(0, pitch * 8), BytesPerRow = pitch, RowsPerImage = 8 }))
            .UseTexture(target, TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead)).UseBuffer(readback, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        graph.ExportBuffer(readback, new(PipelineStage.Host, ResourceAccess.HostRead));
        using CompiledRenderGraph plan = graph.Compile();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        frame.AttachSubmission(submission);
        await submission.WaitAsync();
        commands.Dispose();
        IGraphicsBuffer<byte> buffer = plan.GetBuffer(readback);
        await buffer.MapAsync();
        byte[] bytes = new byte[pitch * 8];
        buffer.CopyTo(bytes);
        buffer.Unmap();
        float red = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, (int)(2 * pitch) + 16));
        if (red != (version == 0 ? 1 : 0) || renderer.RetentionStatistics.CompiledRuns != 1)
        {
            throw new InvalidOperationException($"AOT resident standard={standard} version={version} red={red} compiled={renderer.RetentionStatistics.CompiledRuns}");
        }

        plan.Dispose();
        graph.Dispose();
        frame.Retire();
    }

    Console.WriteLine($"AOT resident standard={standard}: actual graph submit/completion/integer-property/readback/reuse passed");
}
