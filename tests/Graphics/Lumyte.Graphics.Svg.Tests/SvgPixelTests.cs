using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Tests;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Svg.Tests;

/// <summary>Exercises real SVG geometry through the default renderer and GPU readback.</summary>
public sealed class SvgPixelTests
{
    /// <summary>Checks transformed red geometry and root viewport clipping on a native GPU.</summary>
    /// <returns>The asynchronous GPU check.</returns>
    [GpuFact]
    public async Task SolidSvgRendersExpectedPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        var svg = SvgDocument2D.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='4' y='4' width='20' height='20' fill='red'/></svg>"u8);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Translate(new(4, 4));
        svg.Draw(canvas);
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 64, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 1) });
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 64 * 32 * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
        prepared.Record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 64, Height = 32 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = 256, RowsPerImage = 32 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        commands.Dispose();
        await readback.MapAsync();
        byte[] pixels = new byte[64 * 32 * 4];
        readback.CopyTo(pixels);
        readback.Unmap();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.AsSpan(((12 * 64) + 12) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixels.AsSpan(((24 * 64) + 24) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixels.AsSpan(((2 * 64) + 2) * 4, 4).ToArray());
    }
}
