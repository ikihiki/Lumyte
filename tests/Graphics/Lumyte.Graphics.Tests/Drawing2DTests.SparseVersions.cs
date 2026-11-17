using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Independent full-surface old/new sparse version and source-disposal checks.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks frozen sparse versions after source and renderer disposal on Vulkan.</summary>
    /// <returns>The completed GPU verification.</returns>
    [GpuFact]
    public async Task VulkanSparseOldNewSourceDisposalFullPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSparseVersionsAsync(device);
    }

    /// <summary>Checks frozen sparse versions after source and renderer disposal on native wgpu.</summary>
    /// <returns>The completed GPU verification.</returns>
    [GpuFact]
    public async Task WgpuSparseOldNewSourceDisposalFullPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSparseVersionsAsync(device);
    }

    private static async Task CheckSparseVersionsAsync(IGraphicDevice device)
    {
        using IRenderer2D renderer = CreateSparseOracleRenderer(device);
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, SparseVersionContent(false));
        using IGraphicsTexture oldTarget = device.CreateTexture(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTexture newTarget = device.CreateTexture(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView oldView = oldTarget.CreateView();
        using IGraphicsTextureView newView = newTarget.CreateView();
        await InitializeOracleBackgroundAsync(device, oldTarget, oldView);
        await InitializeOracleBackgroundAsync(device, newTarget, newView);
        PreparedDrawingFrame2D old;
        using (DrawingSceneReadLease2D read = scene.Borrow())
        {
            old = await renderer.PrepareAsync(read, new() { View = oldView, LoadOp = AttachmentLoadOp.Load });
        }

        using PreparedDrawingFrame2D oldOwner = old;
        scene.ReplaceContent(node, SparseVersionContent(true));
        scene.SetTransform(node, Matrix3x2.CreateTranslation(2, 0));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D next = await renderer.PrepareAsync(snapshot, new() { View = newView, LoadOp = AttachmentLoadOp.Load });
        snapshot.Dispose();
        scene.Dispose();
        renderer.Dispose();
        Assert.Equal(2, renderer.RetentionStatistics.ActiveFrames);
        await Task.WhenAll(CheckSparseVersionFrameAsync(device, newTarget, next, true), CheckSparseVersionFrameAsync(device, oldTarget, old, false));
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
    }

    private static DrawList2D SparseVersionContent(bool green)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 3, 2), green ? new SRgba<float>(0, 1, 0, 1) : SRgba<float>.Red);
        return canvas.End();
    }

    private static async Task CheckSparseVersionFrameAsync(IGraphicDevice device, IGraphicsTexture target, PreparedDrawingFrame2D frame, bool green)
    {
        uint pitch = OracleRowPitch(device, 8);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = color, After = color });
        var observed = new PartitionCommands(commands);
        frame.Record(observed);
        Assert.Equal(0, observed.ComputePasses);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = _copyRead });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 8, Height = 4 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 4 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        frame.AttachSubmission(submission);
        await submission.WaitAsync();
        commands.Dispose();
        frame.Retire();
        Vector4[] pixels = await DecodeSparseOraclePixelsAsync(readback, 8, 4, pitch);
        Vector4 fill = green ? new(0, 1, 0, 1) : new(1, 0, 0, 1);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int left = green ? 3 : 1;
                AssertPixel(pixels, 8, x, y, x >= left && x < left + 3 && y >= 1 && y < 3 ? fill : new(0, 0, 1, 1));
            }
        }
    }
}
