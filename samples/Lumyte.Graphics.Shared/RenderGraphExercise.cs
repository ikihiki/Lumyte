using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks graph-generated dependencies with exact GPU readback on every backend.</summary>
public static class RenderGraphExercise
{
    private static readonly BarrierScope _copyRead = new(PipelineStage.Copy, ResourceAccess.CopyRead);
    private static readonly BarrierScope _copyWrite = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
    private static readonly BarrierScope _hostRead = new(PipelineStage.Host, ResourceAccess.HostRead);

    /// <summary>Verifies temporary resources, partial writes, owned views, culling and final host synchronization.</summary>
    /// <param name="device">The common backend device.</param>
    /// <returns>The report after exact readback succeeds.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        await CheckBuffersAsync(device);
        await CheckTexturesAsync(device);
        await CheckTextureLayersAsync(device);
        return "Render graph checks passed: temporary buffers/textures, owned views, culling, automatic barriers and exact GPU readback.";
    }

    private static async Task CheckBuffersAsync(IGraphicDevice device)
    {
        uint[] expected = [11, 22, 33, 44, 55, 66, 77, 88];
        using IGraphicsBuffer<uint> upload = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await upload.MapAsync();
        upload.CopyFrom(expected);
        upload.Unmap();
        var graph = new Graph(device, "buffer-readback");
        RenderGraphBuffer<uint> source = graph.ImportBuffer(upload, new(PipelineStage.Host, ResourceAccess.HostWrite), "upload");
        RenderGraphBuffer<uint> temporary = graph.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination }, "temporary");
        RenderGraphBuffer<uint> result = graph.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "readback");
        graph.AddPass("upload-first-half", context => context.Commands.CopyBuffer(context.GetBuffer(source).Slice(0, 4), context.GetBuffer(temporary).Slice(0, 4)))
            .UseBuffer(source, _copyRead).UseBuffer(temporary, _copyWrite);
        graph.AddPass("upload-second-half", context => context.Commands.CopyBuffer(context.GetBuffer(source).Slice(4, 4), context.GetBuffer(temporary).Slice(4, 4)))
            .UseBuffer(source, _copyRead).UseBuffer(temporary, _copyWrite);
        graph.AddPass("readback", context => context.Commands.CopyBuffer(context.GetBuffer(temporary).Slice(0, 8), context.GetBuffer(result).Slice(0, 8)))
            .UseBuffer(temporary, _copyRead).UseBuffer(result, _copyWrite);
        graph.ExportBuffer(result, _hostRead);
        using CompiledRenderGraph plan = graph.Compile();
        Require(plan.PassNames.SequenceEqual(["upload-first-half", "upload-second-half", "readback"]), "A partial buffer producer was culled.");
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        IGraphicsBuffer<uint> readback = plan.GetBuffer(result);
        await readback.MapAsync();
        uint[] actual = new uint[8];
        readback.CopyTo(actual);
        readback.Unmap();
        Require(actual.AsSpan().SequenceEqual(expected), "Graph buffer readback lost values from a partial write.");
    }

    private static async Task CheckTextureLayersAsync(IGraphicDevice device)
    {
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba8Unorm);
        uint pitch = Math.Max(layout.BytesPerRowAlignment, 16);
        var graph = new Graph(device, "texture-layer-readback");
        RenderGraphTexture color = graph.CreateTexture(new() { Width = 4, Height = 3, ArrayLayers = 2, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource }, "color-layers");
        RenderGraphBuffer<byte> result = graph.CreateBuffer<byte>(new() { Count = pitch * 6, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "readback");
        for (uint layer = 0; layer < 2; layer++)
        {
            uint selectedLayer = layer;
            graph.AddPass($"clear-layer-{selectedLayer}", context =>
            {
                IGraphicsTextureView view = context.CreateTextureView(color, new() { BaseArrayLayer = selectedLayer, ArrayLayerCount = 1 });
                ClearColor clear = selectedLayer == 0 ? new(1, 0, 0, 1) : new(0, 1, 0, 1);
                IRenderEncoder render = context.Commands.BeginRenderPass(new()
                {
                    ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = clear }],
                });
                render.End();
            }).UseTexture(color, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        }

        graph.AddPass("readback", context => context.Commands.CopyTextureToBuffer(
            new() { Texture = context.GetTexture(color), Width = 4, Height = 3, ArrayLayerCount = 2 },
            new() { Buffer = context.GetBuffer(result).Slice(0, pitch * 6), BytesPerRow = pitch, RowsPerImage = 3 }))
            .UseTexture(color, TextureState.CopySource, _copyRead).UseBuffer(result, _copyWrite);
        graph.ExportBuffer(result, _hostRead);
        using CompiledRenderGraph plan = graph.Compile();
        Require(plan.PassNames.SequenceEqual(["clear-layer-0", "clear-layer-1", "readback"]), "A texture layer producer was culled.");
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        IGraphicsBuffer<byte> readback = plan.GetBuffer(result);
        await readback.MapAsync();
        byte[] actual = new byte[checked((int)readback.Count)];
        readback.CopyTo(actual);
        readback.Unmap();
        for (uint layer = 0; layer < 2; layer++)
        {
            byte[] pixel = layer == 0 ? [255, 0, 0, 255] : [0, 255, 0, 255];
            for (uint y = 0; y < 3; y++)
            {
                for (uint x = 0; x < 4; x++)
                {
                    int offset = checked((int)((((layer * 3) + y) * pitch) + (x * 4)));
                    Require(actual.AsSpan(offset, 4).SequenceEqual(pixel), "Graph texture readback lost texels from a layer write.");
                }
            }
        }
    }

    private static async Task CheckTexturesAsync(IGraphicDevice device)
    {
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba8Unorm);
        uint pitch = Math.Max(layout.BytesPerRowAlignment, 16);
        var graph = new Graph(device, "texture-readback");
        RenderGraphTexture color = graph.CreateTexture(new() { Width = 4, Height = 3, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource }, "color");
        RenderGraphTexture copied = graph.CreateTexture(new() { Width = 4, Height = 3, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination | TextureUsage.CopySource }, "copied");
        RenderGraphTexture unused = graph.CreateTexture(new() { Width = 4, Height = 3, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination }, "unused");
        RenderGraphBuffer<byte> result = graph.CreateBuffer<byte>(new() { Count = pitch * 3, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "readback");
        graph.AddPass("clear", context =>
        {
            IRenderEncoder render = context.Commands.BeginRenderPass(new()
            {
                ColorAttachments = [new() { View = context.CreateTextureView(color), LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(1, 0, 0, 1) }],
            });
            render.End();
        }).UseTexture(color, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        graph.AddPass("dead", _ => throw new InvalidOperationException("Culled pass was executed."))
            .UseTexture(unused, TextureState.CopyDestination, _copyWrite);
        graph.AddPass("copy", context => context.Commands.CopyTexture(
            new() { Texture = context.GetTexture(color), Width = 4, Height = 3 },
            new() { Texture = context.GetTexture(copied), Width = 4, Height = 3 }))
            .UseTexture(color, TextureState.CopySource, _copyRead).UseTexture(copied, TextureState.CopyDestination, _copyWrite);
        graph.AddPass("readback", context => context.Commands.CopyTextureToBuffer(
            new() { Texture = context.GetTexture(copied), Width = 4, Height = 3 },
            new() { Buffer = context.GetBuffer(result).Slice(0, pitch * 3), BytesPerRow = pitch, RowsPerImage = 3 }))
            .UseTexture(copied, TextureState.CopySource, _copyRead).UseBuffer(result, _copyWrite);
        graph.ExportBuffer(result, _hostRead);
        using CompiledRenderGraph plan = graph.Compile();
        Require(plan.CulledPassNames.SequenceEqual(["dead"]), "Unused graph branch was not culled.");
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        IGraphicsBuffer<byte> readback = plan.GetBuffer(result);
        await readback.MapAsync();
        byte[] actual = new byte[checked((int)readback.Count)];
        readback.CopyTo(actual);
        readback.Unmap();
        byte[] pixel = [255, 0, 0, 255];
        for (uint y = 0; y < 3; y++)
        {
            for (uint x = 0; x < 4; x++)
            {
                Require(actual.AsSpan(checked((int)((y * pitch) + (x * 4))), 4).SequenceEqual(pixel), "Graph texture readback changed the texels.");
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
