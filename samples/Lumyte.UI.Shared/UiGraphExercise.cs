using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Resources;
using Lumyte.Resources.Graphics.Drawing2D;
using static Lumyte.UI.Compose;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.UI.SampleSupport;

internal static class UiGraphExercise
{
    internal static async Task RunAsync(IGraphicDevice device, bool resources)
    {
        await using UiSampleResources? configuration = resources ? await UiSampleResources.CreateAsync(device) : null;
        ResourceReference<Renderer2DResource>? reference = configuration?.Reference;
        if (reference is not null)
        {
            using ResourceLease<Renderer2DResource> lease = reference.Pin();
            if (lease.Value.Resources.Pipeline.Desc.Label != "ui-sample-configured-pipeline")
            {
                throw new InvalidOperationException("The configured resource pipeline was not selected.");
            }
        }

        foreach (bool load in new[] { false, true })
        {
            var description = new TextureDesc { Width = 96, Height = 64, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource };
            using IGraphicsTexture directTarget = device.CreateTexture(description);
            using IGraphicsTexture graphTarget = device.CreateTexture(description);
            byte[] blue = [0, 0, 255, 255];
            byte[]? first = null;
            for (int frame = 0; frame < 3; frame++)
            {
                Definitions.Widget root = frame == 2 ? Panel() : Panel()[Stack(orientation: UiOrientation.Horizontal)[Rectangle(width: frame == 0 ? 8 : 28, height: 12), Rectangle(width: 16, height: 12, fill: new Color2D(1, 0, 0, 0.5f))]];
                using var surface = new UiSurface(root);
                surface.SetViewport(new(64, 32));
                surface.UpdateLayout();
                var direct = new Canvas2D();
                direct.Begin();
                direct.Scale(new(1.5f, 2));
                surface.Record(direct);
                var layered = new RenderGraphCanvas2D();
                layered.Begin();
                layered.Scale(new(1.5f, 2));
                surface.Record(layered);
                byte[] expected = await DrawAsync(device, directTarget, description, frame != 0, direct.End(), null, reference, load);
                byte[] actual = await DrawAsync(device, graphTarget, description, frame != 0, null, layered.End(), reference, load);
                if (!expected.AsSpan().SequenceEqual(actual))
                {
                    throw new InvalidOperationException($"Direct/graph UI pixels differ: frame {frame}, Load={load}.");
                }

                if (first is not null && actual.AsSpan().SequenceEqual(first))
                {
                    throw new InvalidOperationException("Moving/removing the widget did not replace the previous frame.");
                }

                first ??= actual;
                if (frame == 2)
                {
                    for (int i = 0; i < actual.Length; i += 4)
                    {
                        if (!actual.AsSpan(i, 4).SequenceEqual(blue))
                        {
                            throw new InvalidOperationException("Removed UI left stale pixels in the background.");
                        }
                    }
                }
            }
        }

        Console.WriteLine($"UI {(resources ? "Resources + parent graph" : "parent graph")} passed: exact direct/graph pixels, DPI (1.5, 2), Clear/Load, moved/removed UI and background restoration.");
    }

    internal static async Task<byte[]> DrawAsync(IGraphicDevice device, IGraphicsTexture texture, TextureDesc description, bool reused, DrawList2D? direct, RenderGraphDrawList2D? layered, ResourceReference<Renderer2DResource>? reference, bool load, Color2D? clearColor = null)
    {
        uint width = description.Width;
        uint height = description.Height;
        using IGraphicsTextureView view = texture.CreateView();
        uint alignment = device.GetTextureCopyLayout(description.Format).BytesPerRowAlignment;
        uint pitch = (((width * 4) + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * height, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using var graph = new Graph(device, "UI parent frame");
        RenderGraphTexture target = graph.ImportTexture(texture, reused ? TextureState.CopySource : TextureState.Undefined, reused ? new(PipelineStage.Copy, ResourceAccess.CopyRead) : default);
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var host = new BarrierScope(PipelineStage.Host, ResourceAccess.HostRead);
        if (layered is not null && load)
        {
            graph.AddPass("scene.display-linear-background", context => Clear(context.Commands, context.CreateTextureView(target)))
                .UseTexture(target, TextureState.ColorAttachment, color);
        }

        using UiGraphFrame? ui = layered is null ? null : await UiGraphFrame.AppendAsync(graph, layered, new() { Texture = target, Description = description, PixelScale = 1, LoadOp = load ? AttachmentLoadOp.Load : AttachmentLoadOp.Clear, ClearColor = clearColor ?? new(0, 0, 1, 1) }, new(graph), DrawingCommandRenderers2D.CreateDefault(), reference);
        RenderGraphBuffer<byte> output = graph.ImportBuffer(readback, default);
        if (layered is not null)
        {
            graph.AddPass("ui.readback", context => Copy(context.Commands))
                .UseTexture(target, TextureState.CopySource, copy)
                .UseBuffer(output, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
            graph.ExportBuffer(output, host);
        }

        using Renderer2D? renderer = direct is null ? null : reference?.CreateRenderer() ?? new(device, DrawingCommandRenderers2D.CreateDefault());
        using PreparedDraw2D? prepared = direct is null ? null : await renderer!.PrepareAsync(direct, new() { View = view, PixelScale = 1, LoadOp = load ? AttachmentLoadOp.Load : AttachmentLoadOp.Clear, ClearColor = clearColor ?? new(0, 0, 1, 1) });
        using CompiledRenderGraph? plan = layered is null ? null : graph.Compile();
        if (plan is not null && (plan.PassNames[^1] != "ui.readback" || (load && plan.PassNames[0] != "scene.display-linear-background")))
        {
            throw new InvalidOperationException("UI and surrounding passes escaped the parent graph order.");
        }

        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        if (plan is not null)
        {
            plan.Record(commands);
        }
        else
        {
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), Before = reused ? copy : default, BeforeState = reused ? TextureState.CopySource : TextureState.Undefined, AfterState = TextureState.ColorAttachment, After = color });
            if (load)
            {
                Clear(commands, view);
            }

            prepared!.Record(commands);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
            Copy(commands);
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = host });
        }

        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        readback.CopyTo(bytes);
        readback.Unmap();
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            bytes.AsSpan(checked((int)(y * pitch)), (int)width * 4).CopyTo(pixels.AsSpan(y * (int)width * 4));
        }

        return pixels;

        void Copy(IGraphicsCommandBuffer buffer) => buffer.CopyTextureToBuffer(new() { Texture = texture, Width = width, Height = height }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = height });
    }

    private static void Clear(IGraphicsCommandBuffer commands, IGraphicsTextureView view)
    {
        IRenderEncoder pass = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 1, 1) }] });
        pass.End();
    }
}
