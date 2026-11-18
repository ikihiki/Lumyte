using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Tests;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Svg.Tests;

/// <summary>Preserves native rendering conformance formerly hosted in the browser sample.</summary>
public sealed partial class BrowserPaintCoverageTests
{
    /// <summary>Preserves every former browser composite, blend-boundary and gradient pixel oracle on native WebGPU.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public async Task AllCompositeAndGradientPixelsMatchIndependentReferenceAsync()
    {
        using var device = WgpuDevice.Create();
        var canvas = new Canvas2D();
        canvas.Begin();
        BuildBackendPaintChecks(canvas);
        byte[] pixels = await RenderAsync(device, canvas.End());
        CheckBackendPaintPixels(pixels);
    }

    private static async Task<byte[]> RenderAsync(IGraphicDevice device, DrawList2D drawing)
    {
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = Width, Height = Height, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        uint alignment = device.GetTextureCopyLayout(texture.Format).BytesPerRowAlignment;
        uint pitch = (((Width * 4) + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * Height, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        Console.WriteLine("Preparing GPU drawing.");
        using PreparedDraw2D prepared = await renderer.PrepareAsync(drawing, new RenderTarget2D { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new SRgba<float>(1, 1, 1, 1) });
        Console.WriteLine("GPU drawing prepared.");
        using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
        {
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            prepared.Record(commands);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
            commands.CopyTextureToBuffer(new() { Texture = texture, Width = Width, Height = Height }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = Height });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            Console.WriteLine("GPU drawing submitted.");
            await submission.WaitAsync();
            Console.WriteLine("GPU drawing completed.");
        }

        Console.WriteLine("Mapping GPU readback.");
        await readback.MapAsync();
        Console.WriteLine("GPU readback mapped.");
        byte[] padded = new byte[checked((int)readback.Count)];
        readback.CopyTo(padded);
        readback.Unmap();
        byte[] packed = new byte[checked((int)(Width * Height * 4))];
        for (uint y = 0; y < Height; y++)
        {
            padded.AsSpan(checked((int)(y * pitch)), checked((int)(Width * 4))).CopyTo(packed.AsSpan(checked((int)(y * Width * 4))));
        }

        return packed;
    }
}
