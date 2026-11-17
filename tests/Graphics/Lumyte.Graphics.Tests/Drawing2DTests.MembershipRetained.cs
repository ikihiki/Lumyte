using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Completion-safe retained ownership with frame-private membership output.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks two simultaneously owned retained frames and completion-safe reuse on Vulkan.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task VulkanMembershipRetainedOraclesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckMembershipRetainedOraclesAsync(device);
    }

    /// <summary>Checks two simultaneously owned retained frames and completion-safe reuse on wgpu.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task WgpuMembershipRetainedOraclesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckMembershipRetainedOraclesAsync(device);
    }

    private static async Task CheckMembershipRetainedOraclesAsync(IGraphicDevice device)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        var path = new PathBuilder2D();
        path.MoveTo(new(2, 1));
        path.LineTo(new(6, 1));
        path.LineTo(new(6, 3));
        path.LineTo(new(2, 3));
        path.Close();
        canvas.FillPath(path.Build(), PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 0.5f)));
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, canvas.End());
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        scene.Dispose();
        foreach (int backend in new[] { 0, 1 })
        {
            using IRenderer2D renderer = backend == 0 ? new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault()) : CreateMembershipOracleRenderer(device);

            // Prepare two exclusive owners before either retires, then reuse a released slot.
            // In-flight frames have private scratch; only completed assets become reusable.
            using IGraphicsTexture firstTarget = device.CreateTexture(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTexture secondTarget = device.CreateTexture(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView firstView = firstTarget.CreateView();
            using IGraphicsTextureView secondView = secondTarget.CreateView();
            await InitializeOracleBackgroundAsync(device, firstTarget, firstView);
            await InitializeOracleBackgroundAsync(device, secondTarget, secondView);
            using PreparedDrawingFrame2D first = await renderer.PrepareAsync(snapshot, new() { View = firstView, LoadOp = AttachmentLoadOp.Load });
            using PreparedDrawingFrame2D second = await renderer.PrepareAsync(snapshot, new() { View = secondView, LoadOp = AttachmentLoadOp.Load });
            Assert.Equal(2, renderer.RetentionStatistics.ActiveFrames);
            await ExecuteOracleRetainedFrameAsync(device, firstTarget, first);
            Assert.Equal(1, renderer.RetentionStatistics.ActiveFrames);
            await ExecuteOracleRetainedFrameAsync(device, secondTarget, second);
            Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
            using IGraphicsTexture reusedTarget = device.CreateTexture(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView reusedView = reusedTarget.CreateView();
            await InitializeOracleBackgroundAsync(device, reusedTarget, reusedView);
            using PreparedDrawingFrame2D reused = await renderer.PrepareAsync(snapshot, new() { View = reusedView, LoadOp = AttachmentLoadOp.Load });
            Assert.Equal(1, renderer.RetentionStatistics.ActiveFrames);
            Assert.Equal(backend == 0 ? 2 : 3, renderer.RetentionStatistics.CompiledRuns);
            Assert.Equal(backend == 0 ? 1 : 0, renderer.RetentionStatistics.ReusedRuns);
            await ExecuteOracleRetainedFrameAsync(device, reusedTarget, reused);
            Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        }
    }

    private static async Task ExecuteOracleRetainedFrameAsync(IGraphicDevice device, IGraphicsTexture target, PreparedDrawingFrame2D frame)
    {
        uint pitch = OracleRowPitch(device, 8);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
        frame.Record(commands);
        Assert.Throws<InvalidOperationException>(() => frame.Record(commands));
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = attachment, After = _copyRead });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 8, Height = 4 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 4 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        frame.AttachSubmission(submission);
        await submission.WaitAsync();
        Assert.Throws<InvalidOperationException>(frame.Retire);
        commands.Dispose();
        frame.Retire();
        Vector4[] pixels = await DecodeMembershipOraclePixelsAsync(readback, 8, 4, pitch);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Vector4 expected = x >= 2 && x < 6 && y >= 1 && y < 3 ? new(0.5f, 0, 0.5f, 1) : new(0, 0, 1, 1);
                AssertPixel(pixels, 8, x, y, expected);
            }
        }
    }
}
