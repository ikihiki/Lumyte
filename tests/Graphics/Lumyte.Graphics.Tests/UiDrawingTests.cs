using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.UI;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks UI clips, hosted surfaces and complete frame redraw using actual backend pixels.</summary>
public sealed class UiDrawingTests
{
    /// <summary>Checks direct UI rendering and successive frames on Vulkan.</summary>
    /// <returns>The asynchronous pixel verification.</returns>
    [GpuFact]
    public async Task VulkanUiClippingAndRedrawAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckFramesAsync(device);
    }

    /// <summary>Checks direct UI rendering and successive frames on wgpu.</summary>
    /// <returns>The asynchronous pixel verification.</returns>
    [GpuFact]
    public async Task WgpuUiClippingAndRedrawAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckFramesAsync(device);
    }

    private static async Task CheckFramesAsync(IGraphicDevice device)
    {
        Definitions.Stack stack = Stack(width: 12, height: 10, clipToBounds: true)[
            Rectangle(width: 8, height: 8, fill: new Color2D(1, 0, 0, 1)),
            Rectangle(width: 8, height: 8, fill: new Color2D(0, 0, 1, 0.5f))];
        Definitions.Panel root = Panel()[stack];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(16, 16));
        using var hosted = new UiSurface(Rectangle(fill: new Color2D(0, 1, 0, 0.5f)));
        hosted.SetViewport(new(4, 4));
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        for (int frame = 0; frame < 4; frame++)
        {
            if (frame == 2)
            {
                stack.ClipToBounds = false;
                stack.Spacing = 2;
            }
            else if (frame == 3)
            {
                root.Children = [];
            }

            var canvas = new Canvas2D();
            canvas.Begin();
            surface.Record(canvas);
            canvas.Translate(new(10, 10));
            hosted.Record(canvas);
            Vector4[] pixels = await RenderFrameAsync(device, renderer, target, view, canvas.End(), frame == 0);
            AssertPixel(pixels, 2, 2, frame == 3 ? new(0, 0, 0, 1) : new(1, 0, 0, 1));
            AssertPixel(pixels, 2, 9, frame < 2 ? new(0, 0, 0.5f, 1) : new(0, 0, 0, 1));
            AssertPixel(pixels, 2, 12, frame == 2 ? new(0, 0, 0.5f, 1) : new(0, 0, 0, 1));
            AssertPixel(pixels, 11, 11, new(0, 0.5f, 0, 1));
            AssertPixel(pixels, 15, 15, new(0, 0, 0, 1));
        }
    }

    private static async Task<Vector4[]> RenderFrameAsync(IGraphicDevice device, Renderer2D renderer, IGraphicsTexture target, IGraphicsTextureView view, DrawList2D list, bool firstFrame)
    {
        using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 1) });
        uint alignment = device.GetTextureCopyLayout(target.Format).BytesPerRowAlignment;
        uint pitch = (((16 * 8) + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 16, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var colorScope = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        commands.Barrier(new TextureBarrierDesc
        {
            Texture = target,
            Range = new(0, 1, 0, 1),
            BeforeState = firstFrame ? TextureState.Undefined : TextureState.CopySource,
            AfterState = TextureState.ColorAttachment,
            Before = firstFrame ? default : copyRead,
            After = colorScope,
        });
        prepared.Record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = colorScope, After = copyRead });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 16, Height = 16 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 16 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        readback.CopyTo(bytes);
        readback.Unmap();
        var pixels = new Vector4[16 * 16];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int offset = checked((int)((y * pitch) + (x * 8)));
                pixels[(y * 16) + x] = new(ReadHalf(offset), ReadHalf(offset + 2), ReadHalf(offset + 4), ReadHalf(offset + 6));
            }
        }

        return pixels;

        float ReadHalf(int offset) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)));
    }

    private static void AssertPixel(Vector4[] pixels, int x, int y, Vector4 expected)
    {
        Vector4 actual = pixels[(y * 16) + x];
        var error = Vector4.Abs(actual - expected);
        Assert.True(error.X < 0.002f && error.Y < 0.002f && error.Z < 0.002f && error.W < 0.002f, $"Pixel ({x}, {y}): expected {expected}, actual {actual}.");
    }
}
