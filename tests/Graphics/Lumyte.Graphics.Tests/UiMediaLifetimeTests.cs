using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.UI;
using Lumyte.UI.Media;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks borrowed media lifetime and actual pixels on explicitly enabled native backends.</summary>
public sealed class UiMediaLifetimeTests
{
    /// <summary>Runs media ownership, preparation and snapshot equivalence on Vulkan.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task VulkanBorrowedMediaSurvivesUntilGpuCompletionAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAsync(device);
    }

    /// <summary>Runs media ownership, preparation and snapshot equivalence on wgpu.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task WgpuBorrowedMediaSurvivesUntilGpuCompletionAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAsync(device);
    }

    private static async Task CheckAsync(IGraphicDevice device)
    {
        using var owner = new ImageOwner(device);
        await UploadAsync(device, owner.Texture);
        var source = new PreparedImage(new(owner.View, owner.Sampler), new(2, 1), new(0, 0, 2, 1));
        using var surface = new UiSurface(Stack()[Image(source: source, width: 8, height: 4), Icon(source: IconSource.Image(source), size: 8)]);
        surface.SetViewport(new(16, 16));
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        DrawList2D direct = canvas.End();
        using UiDrawingScene2D adapter = surface.CreateDrawingScene();
        adapter.Update();
        using DrawingSnapshot2D capture = adapter.Scene.CaptureSnapshot();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView targetView = target.CreateView();
        var renderTarget = new RenderTarget2D { View = targetView, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 1) };
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await renderer.PrepareAsync(direct, renderTarget, canceled.Token));
        await Assert.ThrowsAsync<ArgumentException>(async () => await renderer.PrepareAsync(direct, renderTarget with { PixelScale = 0 }));
        Assert.Equal(0, owner.Disposals);
        Vector4[] expected = await RenderFrameAsync(device, renderer, target, targetView, direct, true, () =>
        {
            surface.Rebuild(Panel());
            adapter.Update();
            surface.Dispose();
            adapter.Dispose();
            Assert.Equal(0, owner.Disposals);
        });
        AssertPixel(expected, 1, 1, new(1, 0, 0, 1));
        AssertPixel(expected, 6, 1, new(0, 1, 0, 1));
        AssertPixel(expected, 1, 7, new(1, 0, 0, 1));
        AssertPixel(expected, 6, 7, new(0, 1, 0, 1));
        AssertPixel(expected, 1, 5, new(0, 0, 0, 1));
        using PreparedDrawingFrame2D retained = await renderer.PrepareAsync(capture, renderTarget);
        uint alignment = device.GetTextureCopyLayout(target.Format).BytesPerRowAlignment;
        uint pitch = ((128 + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 16, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopySource, AfterState = TextureState.ColorAttachment, Before = copyRead, After = color });
        retained.Record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copyRead });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 16, Height = 16 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 16 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        retained.AttachSubmission(submission);
        Assert.Throws<InvalidOperationException>(retained.Dispose);
        Assert.Equal(0, owner.Disposals);
        await submission.WaitAsync();
        commands.Dispose();
        retained.Retire();
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        readback.CopyTo(bytes);
        readback.Unmap();
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int offset = checked((int)((y * pitch) + (x * 8)));
                Vector4 actual = new(ReadHalf(offset), ReadHalf(offset + 2), ReadHalf(offset + 4), ReadHalf(offset + 6));
                Assert.True(Vector4.Distance(actual, expected[(y * 16) + x]) < 0.002f, $"Direct/captured pixel mismatch at {x},{y}.");
            }
        }

        capture.Dispose();
        renderer.Dispose();
        Assert.Equal(0, owner.Disposals);
        owner.Dispose();
        Assert.Equal(1, owner.Disposals);

        float ReadHalf(int offset) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)));
    }

    private static async Task UploadAsync(IGraphicDevice device, IGraphicsTexture texture)
    {
        uint pitch = device.GetTextureCopyLayout(texture.Format).BytesPerRowAlignment;
        pitch = ((8 + pitch - 1) / pitch) * pitch;
        byte[] bytes = new byte[pitch];
        new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }.CopyTo(bytes, 0);
        using IGraphicsBuffer<byte> upload = device.CreateBuffer<byte>(new() { Count = pitch, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await upload.MapAsync();
        upload.CopyFrom(bytes);
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = upload.SliceElements(0, upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = default, After = copyWrite });
        commands.CopyBufferToTexture(new() { Buffer = upload.SliceElements(0, upload.Count), BytesPerRow = pitch, RowsPerImage = 1 }, new() { Texture = texture, Width = 2, Height = 1 });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.Sampled, Before = copyWrite, After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
    }

    private static async Task<Vector4[]> RenderFrameAsync(IGraphicDevice device, Renderer2D renderer, IGraphicsTexture target, IGraphicsTextureView view, DrawList2D list, bool firstFrame, Action? submitted = null)
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
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 16, Height = 16 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 16 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        submitted?.Invoke();
        await submission.WaitAsync();
        commands.Dispose();
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

    private static void AssertPixel(Vector4[] pixels, int x, int y, Vector4 expected) => Assert.True(Vector4.Distance(pixels[(y * 16) + x], expected) < 0.002f, $"Unexpected media pixel at {x},{y}: {pixels[(y * 16) + x]}.");

    private sealed class ImageOwner : IDisposable
    {
        internal ImageOwner(IGraphicDevice device)
        {
            Texture = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination | TextureUsage.Sampled });
            View = Texture.CreateView();
            Sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest });
        }

        internal IGraphicsTexture Texture { get; }

        internal IGraphicsTextureView View { get; }

        internal IGraphicsSampler Sampler { get; }

        internal int Disposals { get; private set; }

        public void Dispose()
        {
            if (Disposals == 0)
            {
                Sampler.Dispose();
                View.Dispose();
                Texture.Dispose();
                Disposals++;
            }
        }
    }
}
