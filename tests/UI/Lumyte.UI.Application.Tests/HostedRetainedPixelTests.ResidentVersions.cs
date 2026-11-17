using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Tests;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks real hosted GPU versions without trusting the host's constant snapshot revision.</summary>
public sealed partial class HostedRetainedPixelTests
{
    /// <summary>Ordinary host content, outer placement, visibility and disposed UI keep frozen GPU versions correct.</summary>
    /// <returns>The native GPU verification.</returns>
    [GpuFact]
    public async Task WgpuHostedRevisionZeroMutationsKeepGpuVersionsAsync()
    {
        using var native = WgpuDevice.Create();
        var device = new ObservedDevice(native);
        using var presenter = new UiHostPresenter(device, default, 3);
        var color = Signal.Create(new SRgba<float>(1, 0, 0, 1));
        using var surface = new UiSurface(Panel()[Rectangle(key: "changing", width: 12, height: 12, fill: color), Rectangle(key: "stable", width: 5, height: 5, fill: new SRgba<float>(0, 0, 1, 0.5f))]);
        surface.SetViewport(new(16, 16));
        var state = new UiWindowSourceSnapshot(1, null, new(16, 16), Vector2.One, true, true, false, Guid.NewGuid());
        var source = new UiHostedDrawingSource(() => state);
        var view = new UiWindowPresentationView(new(source.SourceId, 1), source.SourceId, UiWindowRole.Normal, new(0, 0, 16, 16), new(0, 0, 16, 16), Vector2.One, 1, true);
        await using IUiWindowPresentation presentation = await source.PreparePresentationAsync(view, surface, default);
        using var swapchain = new RecordingSwapchain(device);
        using DrawingSnapshot2D original = Capture();
        DrawList2D originalDirect = Direct();
        (byte[] Pixels, IShaderReference[] References, ulong Copies) warm = await Draw(original);
        Assert.True(warm.Copies > 0);
        Assert.True(warm.References.Length >= 2);
        Assert.Equal(await ReadHostedDirectAsync(native, originalDirect), warm.Pixels);
        using DrawingSnapshot2D unchanged = Capture();
        (byte[] Pixels, IShaderReference[] References, ulong Copies) noOp = await Draw(unchanged);
        Assert.Equal(0ul, noOp.Copies);
        Assert.Equal(warm.Pixels, noOp.Pixels);
        Assert.Equal(warm.References.Length, noOp.References.Length);
        for (int i = 0; i < warm.References.Length; i++)
        {
            Assert.Same(warm.References[i].Resource, noOp.References[i].Resource);
            Assert.Same(warm.References[i].Table, noOp.References[i].Table);
        }

        color.Value = new(0, 1, 0, 1);
        using DrawingSnapshot2D changed = Capture();
        (byte[] Pixels, IShaderReference[] References, ulong Copies) leaf = await Draw(changed);
        Assert.InRange(leaf.Copies, 1ul, warm.Copies - 1);
        Assert.Same(warm.References[^1].Resource, leaf.References[^1].Resource);
        Assert.Same(warm.References[^1].Table, leaf.References[^1].Table);
        Assert.NotSame(warm.References[0].Resource, leaf.References[0].Resource);
        Assert.Equal(await ReadHostedDirectAsync(native, Direct()), leaf.Pixels);

        presentation.Update(view with { BoundsInPresentation = new(1, 2, 16, 16), ClipInPresentation = new(3, 1, 10, 12), Scale = new(1.25f, 1) });
        using DrawingSnapshot2D placed = Capture();
        (byte[] Pixels, IShaderReference[] References, ulong Copies) moved = await Draw(placed);
        Assert.True(moved.Copies > 0);
        Assert.Equal(await ReadHostedDirectAsync(native, Direct()), moved.Pixels);
        presentation.Update(view with { IsVisible = false });
        using DrawingSnapshot2D hidden = Capture();
        (byte[] Pixels, IShaderReference[] References, ulong Copies) absent = await Draw(hidden);
        Assert.Equal(0ul, absent.Copies);
        Assert.Empty(absent.References);
        Assert.Equal(await ReadHostedDirectAsync(native, Direct()), absent.Pixels);
        presentation.Update(view);
        using DrawingSnapshot2D shown = Capture();
        (byte[] Pixels, IShaderReference[] References, ulong Copies) restored = await Draw(shown);

        // Returning to an earlier rectangle constructs a new immutable outer clip path.
        // The current dependency-keyed route rebuilds both derived runs, not their pixels.
        Assert.Equal(warm.Copies, restored.Copies);
        Assert.Equal(leaf.Pixels, restored.Pixels);
        await presentation.DisposeAsync();
        surface.Dispose();
        (byte[] Pixels, IShaderReference[] References, ulong Copies) old = await Draw(original);
        Assert.Equal(0ul, old.Copies);
        Assert.Equal(warm.Pixels, old.Pixels);
        Assert.Equal(await ReadHostedDirectAsync(native, originalDirect), old.Pixels);
        Assert.Same(warm.References[0].Resource, old.References[0].Resource);
        Console.WriteLine($"hostedResident revision=0 coldBytes={warm.Copies} noOpBytes={noOp.Copies} dirtyLeafBytes={leaf.Copies} clippedPlacementBytes={moved.Copies} hiddenBytes={absent.Copies} restoredBytes={restored.Copies} oldAfterUiDisposeBytes={old.Copies}");

        DrawingSnapshot2D Capture()
        {
            DrawingSnapshot2D snapshot = source.RecordRetainedFrame(0);
            source.CompleteFrame();
            Assert.Equal(0ul, snapshot.Revision);
            return snapshot;
        }

        DrawList2D Direct()
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            source.Record(canvas);
            source.CompleteFrame();
            return canvas.End();
        }

        async Task<(byte[] Pixels, IShaderReference[] References, ulong Copies)> Draw(DrawingSnapshot2D snapshot)
        {
            ulong before = device.PayloadCopyBytes;
            device.RecordReferences.Clear();
            byte[]? pixels = null;
            Assert.Equal(SurfaceStatus.Success, await presenter.DrawAsync(swapchain, snapshot, Guid.NewGuid(), value => pixels = value.Pixels));
            Assert.NotNull(pixels);
            return (pixels, device.RecordReferences.ToArray(), device.PayloadCopyBytes - before);
        }
    }

    private static async Task<byte[]> ReadHostedDirectAsync(IGraphicDevice device, DrawList2D content)
    {
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        using PreparedDraw2D prepared = await renderer.PrepareAsync(content, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment;
        uint pitch = ((64 + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 16, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
        prepared.Record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 16, Height = 16 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 16 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        commands.Dispose();
        await readback.MapAsync();
        byte[] padded = new byte[readback.Count];
        readback.CopyTo(padded);
        readback.Unmap();
        byte[] pixels = new byte[16 * 16 * 4];
        for (int row = 0; row < 16; row++)
        {
            padded.AsSpan((int)(row * pitch), 64).CopyTo(pixels.AsSpan(row * 64));
        }

        return pixels;
    }
}
