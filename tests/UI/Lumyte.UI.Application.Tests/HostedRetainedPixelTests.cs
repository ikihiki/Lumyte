using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Tests;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks retained hosted placements against independently recorded direct GPU pixels.</summary>
public sealed partial class HostedRetainedPixelTests
{
    /// <summary>Checks staged preparation and visual-image updates use retained GPU owners.</summary>
    /// <returns>The asynchronous GPU verification.</returns>
    [GpuFact]
    public async Task WgpuCandidateAndVisualLayersUseRetainedFramesAsync()
    {
        using var native = WgpuDevice.Create();
        var device = new ObservedDevice(native);
        using var presenter = new UiHostPresenter(device, default);
        using var surface = new UiSurface(new PathPanel());
        surface.SetViewport(new(16, 16));
        using UiDrawingScene2D retained = surface.CreateDrawingScene();
        retained.Update();
        using DrawingSnapshot2D candidate = retained.Scene.CaptureSnapshot();
        await presenter.PrepareCandidateAsync(candidate, 16, 16);
        Assert.Equal(0, device.Dispatches);
        Assert.NotEmpty(device.Tables);
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        DrawList2D fragment = canvas.End();
        using var reservation = new Reservation();
        using UiVisualSnapshot? image = await presenter.CaptureVisualAsync(fragment, 16, 16, reservation);
        Assert.NotNull(image);
        Assert.False(reservation.Released);
        Assert.True(device.Dispatches > 0);
        int captures = device.Dispatches;

        // Identical completed content reuses published membership without another dispatch.
        await presenter.UpdateVisualAsync(image, fragment, []);
        Assert.Equal(captures, device.Dispatches);
        var changed = new Canvas2D();
        changed.Begin();
        changed.Translate(new(1, 0));
        surface.Record(changed);
        await presenter.UpdateVisualAsync(image, changed.End(), []);
        Assert.True(device.Dispatches > captures);
        await presenter.DrainVisualsAsync();
        using IDisposable retainedImage = image.Retain();
        image.Dispose();
        Assert.False(reservation.Released);
        retainedImage.Dispose();
        Assert.True(reservation.Released);
    }

    /// <summary>Checks reuse, translucent overlap, clip, DPI, placement, visibility and generation rebuild.</summary>
    /// <returns>The asynchronous GPU verification.</returns>
    [GpuFact]
    public async Task WgpuHostedRetainedMatchesDirectPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        using IRenderer2D renderer = Renderer2D.CreateDefault(device, retentionOptions: new() { EnableResidentInstances = true });
        using var directRenderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var surface = new UiSurface(Panel()[Rectangle(width: 14, height: 12, fill: new SRgba<float>(1, 0, 0, 1)), Rectangle(width: 9, height: 18, fill: new SRgba<float>(0, 0, 1, 0.5f))]);
        using var overlay = new UiSurface(Rectangle(fill: new SRgba<float>(0, 1, 0, 0.5f)));
        surface.SetViewport(new(20, 20));
        overlay.SetViewport(new(10, 10));
        var state = new UiWindowSourceSnapshot(1, null, new(64, 48), Vector2.One, true, true, false, Guid.NewGuid());
        var source = new UiHostedDrawingSource(() => state);
        var view = new UiWindowPresentationView(new(source.SourceId, 1), source.SourceId, UiWindowRole.Normal, new(2, 3, 20, 20), new(0, 0, 40, 30), new(1.5f, 1.5f), 1, true);
        UiWindowPresentationView top = view with { Window = new(source.SourceId, 2), BoundsInPresentation = new(5, 6, 10, 10), StackingOrder = 1 };
        await using IUiWindowPresentation one = await source.PreparePresentationAsync(view, surface, default);
        await using IUiWindowPresentation two = await source.PreparePresentationAsync(top, overlay, default);
        for (int frame = 0; frame < 5; frame++)
        {
            if (frame == 2)
            {
                surface.Rebuild(Panel(clipToBounds: true)[Rectangle(width: 12, height: 9, fill: new SRgba<float>(0, 0, 1, 1))]);
                view = view with { BoundsInPresentation = new(7, 4, 20, 20), ClipInPresentation = new(9, 1, 12, 16), Scale = new(2, 1.5f) };
                one.Update(view);
            }

            top = top with { IsVisible = frame != 3 };
            two.Update(top);
            using DrawingSnapshot2D scene = source.RecordRetainedFrame(frame == 4 ? 1ul : 0ul);
            source.CompleteFrame();
            var canvas = new Canvas2D();
            canvas.Begin();
            Record(surface, view);
            if (top.IsVisible)
            {
                Record(overlay, top);
            }

            DrawList2D direct = canvas.End();
            byte[] actual = await RenderAsync(scene, null, renderer);
            byte[] expected = await RenderAsync(null, direct, directRenderer);
            Assert.Equal(expected, actual);

            void Record(UiSurface content, UiWindowPresentationView placement)
            {
                canvas.Save();
                canvas.Scale(placement.Scale);
                canvas.ClipRect(placement.ClipInPresentation);
                canvas.Translate(new(placement.BoundsInPresentation.X, placement.BoundsInPresentation.Y));
                content.Record(canvas);
                canvas.Restore();
            }
        }

        Assert.True(renderer.RetentionStatistics.ReusedRuns > 0);

        async Task<byte[]> RenderAsync(DrawingSnapshot2D? scene, DrawList2D? direct, IRenderer2D draw)
        {
            using IGraphicsTexture target = device.CreateTexture(new() { Width = 64, Height = 48, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView targetView = target.CreateView();
            var description = new RenderTarget2D { View = targetView, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 1) };
            PreparedDrawingFrame2D? retained = scene is null ? null : await draw.PrepareAsync(scene, description);
            using PreparedDraw2D? prepared = direct is null ? null : await draw.PrepareAsync(direct, description);
            using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 64 * 48 * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            if (retained is not null)
            {
                retained.Record(commands);
            }
            else
            {
                prepared!.Record(commands);
            }

            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
            commands.CopyTextureToBuffer(new() { Texture = target, Width = 64, Height = 48 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = 256, RowsPerImage = 48 });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            retained?.AttachSubmission(submission);
            await submission.WaitAsync();
            commands.Dispose();
            retained?.Retire();
            retained?.Dispose();
            await readback.MapAsync();
            byte[] bytes = new byte[checked((int)readback.Count)];
            readback.CopyTo(bytes);
            readback.Unmap();
            return bytes;
        }
    }

    /// <summary>Ordinary host drawing records Standard compute work before actual native submission.</summary>
    /// <returns>The real-device host route check.</returns>
    [GpuFact]
    public async Task WgpuOrdinaryPresenterSelectsStandardAsync()
    {
        using var native = WgpuDevice.Create();
        var device = new ObservedDevice(native);
        using var presenter = new UiHostPresenter(device, default, 1);
        using var surface = new UiSurface(new PathPanel());
        surface.SetViewport(new(16, 16));
        using UiDrawingScene2D retained = surface.CreateDrawingScene();
        retained.Update();
        using DrawingSnapshot2D snapshot = retained.Scene.CaptureSnapshot();
        using var swapchain = new RecordingSwapchain(device);
        Assert.Equal(SurfaceStatus.Success, await presenter.DrawAsync(swapchain, snapshot, Guid.NewGuid()));
        Assert.True(device.Dispatches > 0);
        IShaderReference[] originalPayloads = device.RecordReferences.ToArray();
        Assert.NotEmpty(originalPayloads);
        device.RecordReferences.Clear();
        int dispatches = device.Dispatches;
        ulong payloadBytes = device.PayloadCopyBytes;
        Assert.True(payloadBytes > 0);
        Assert.Equal(SurfaceStatus.Success, await presenter.DrawAsync(swapchain, snapshot, Guid.NewGuid()));
        Assert.Equal(dispatches, device.Dispatches);
        Assert.Equal(payloadBytes, device.PayloadCopyBytes);
        Assert.Equal(originalPayloads.Length, device.RecordReferences.Count);
        for (int i = 0; i < originalPayloads.Length; i++)
        {
            Assert.Same(originalPayloads[i].Resource, device.RecordReferences[i].Resource);
            Assert.Same(originalPayloads[i].Table, device.RecordReferences[i].Table);
        }

        Console.WriteLine($"ordinaryHost coldPayloadBytes={payloadBytes} noOpPayloadBytes={device.PayloadCopyBytes - payloadBytes} noOpDispatches={device.Dispatches - dispatches}");
    }

    private sealed class RecordingSwapchain : IGraphicsSwapchain
    {
        private readonly IGraphicsTexture _texture;

        internal RecordingSwapchain(IGraphicDevice device)
        {
            _texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
        }

        public SwapchainDesc Configuration { get; } = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment };

        public void Reconfigure(SwapchainDesc desc) => throw new NotSupportedException();

        public ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(new SurfaceAcquireResult(SurfaceStatus.Success, new RecordingImage(_texture)));

        public void Dispose() => _texture.Dispose();
    }

    private sealed class RecordingImage(IGraphicsTexture texture) : IGraphicsSurfaceFrame
    {
        public SurfaceFrameStatus Status => SurfaceFrameStatus.Acquired;

        public IGraphicsTexture Texture => texture;

        public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null) => SurfaceStatus.Success;

        public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class PathPanel : Definitions.Panel
    {
        protected override UiElement CreateElement() => new PathElement(this);
    }

    private sealed class PathElement(PathPanel widget) : PanelElement(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            var path = new PathBuilder2D();
            path.MoveTo(new(2, 2));
            path.LineTo(new(14, 2));
            path.LineTo(new(8, 14));
            path.Close();
            canvas.FillPath(path.Build(), new SRgba<float>(1, 0, 0, 1));
        }
    }

    private sealed class Reservation : IDisposable
    {
        internal bool Released { get; private set; }

        public void Dispose() => Released = true;
    }
}
