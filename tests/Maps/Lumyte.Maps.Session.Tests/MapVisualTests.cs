using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Maps.UI;
using Lumyte.Mathematics.Colors;
using Lumyte.Memory;
using Lumyte.Resources;
using Lumyte.UI;
using Xunit;

namespace Lumyte.Maps.Session.Tests;

/// <summary>Analytic hardware pixels for tile ownership, host DPI, UI parent bounds and overlay order.</summary>
public sealed class MapVisualTests
{
    /// <summary>Executes both host DPI scales against independent known interior and seam colors.</summary>
    /// <returns>The actual GPU readback checks.</returns>
    [MapGpuFact]
    public async Task AdjacentExtentsTransparencyDpiParentClipAndOverlayAsync()
    {
        using var device = VulkanDevice.Create();
        foreach ((float Dpi, double Longitude) scenario in new[] { (1f, 0.0), (2f, 0.0), (1.5f, 0.0), (1f, 0.17578125), (2f, 0.17578125), (1.5f, 0.17578125) })
        {
            float dpi = scenario.Dpi;
            await using var memory = new MemoryManager();
            await using var manager = new ResourceManager("visual-map", new Source(), [new Stage()], memory, new Runtime());
            var style = new MapStyle("visual", "v1", new(0, 0, 0, 1), [
                new("water", new(0, 0, 1, 0.5f)),
                new("roads", new(1, 0, 0, 1), new StrokeStyle2D { Width = 8, Cap = LineCap2D.Round }),
                new("roads", new(1, 1, 1, 1), new StrokeStyle2D { Width = 4, Cap = LineCap2D.Round }),
            ]);
            await using var map = new MapSession(manager, new Tiles(), style, new(), [new(1, 0, 0), new(1, 1, 0)]);
            map.SetCamera(new(scenario.Longitude, 66.51326044311186, 1));
            Compose.Definitions.Panel panel = Compose.Panel(width: 384, height: 128, clipToBounds: true)[
                new MapView { Session = map },
                Compose.Rectangle(width: 16, height: 16, fill: new SRgba<float>(0, 1, 0, 1)),
                new OverflowWidget()];
            using var surface = new UiSurface(Compose.Panel()[panel]);
            surface.SetViewport(new(512, 128));
            var initial = new Canvas2D();
            initial.Begin();
            surface.Record(initial);
            _ = initial.End();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (map.PublishedTiles != 2)
            {
                map.Advance();
                await Task.Delay(1, deadline.Token);
            }

            var canvas = new Canvas2D();
            canvas.Begin();
            surface.Record(canvas);
            DrawList2D direct = canvas.End();
            using UiDrawingScene2D scene = surface.CreateDrawingScene();
            scene.Update();
            scene.Update();
            Assert.Equal(0, scene.LastRecordingCount);
            using DrawingSceneReadLease2D borrow = scene.Borrow();
            byte[] first = await RenderAsync(device, direct, null, dpi);
            byte[] retained = await RenderAsync(device, null, borrow, dpi);
            await File.WriteAllBytesAsync($"/tmp/lumyte-map-dpi-{dpi}-{scenario.Longitude}.rgba", first);
            Assert.Equal(first, retained);
            Pixel(first, dpi, 8, 8, 0, 255, 0);
            Pixel(first, dpi, 400, 40, 255, 0, 255);
            Pixel(first, dpi, 380, 115, 255, 255, 0);
            Pixel(first, dpi, 400, 115, 255, 0, 255);
            Pixel(first, dpi, 100, 32, 0, 0, 188);
            Pixel(first, dpi, 191, 32, 0, 0, 188);
            Pixel(first, dpi, 192, 32, 0, 0, 188);
            Pixel(first, dpi, 193, 32, 0, 0, 188);
            Pixel(first, dpi, 191, 64, 255, 255, 255);
            Pixel(first, dpi, 192, 64, 255, 255, 255);
            Pixel(first, dpi, 193, 64, 255, 255, 255);
            Pixel(first, dpi, 192, 67, 255, 0, 0);
        }
    }

    /// <summary>A fractional source-tile scale must not double-own the pixel centered on a shared edge.</summary>
    /// <returns>The independent analytic seam oracle.</returns>
    [MapGpuFact]
    public async Task FractionalZoomSharedEdgeOwnsEachPixelOnceAsync()
    {
        using var device = VulkanDevice.Create();
        await using var memory = new MemoryManager();
        await using var manager = new ResourceManager("fractional-seam", new Source(), [new Stage()], memory, new Runtime());
        var style = new MapStyle("visual", "v1", new(0, 0, 0, 1), [new("water", new(0, 0, 1, 0.5f))]);
        await using var map = new MapSession(manager, new Tiles(), style, new(), [new(1, 0, 0), new(1, 1, 0)]);
        map.SetViewport(new(512, 128));
        map.SetCamera(new(-0.13321727635345296, 66.51326044311186, 1.4));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (map.PublishedTiles != 2)
        {
            map.Advance();
            await Task.Delay(1, deadline.Token);
        }

        var canvas = new Canvas2D();
        canvas.Begin();
        map.Record(canvas);
        byte[] pixels = await RenderAsync(device, canvas.End(), null, 1);
        for (int x = 254; x <= 258; x++)
        {
            Pixel(pixels, 1, x, 32, 0, 0, 188);
        }
    }

    /// <summary>A deterministic public camera path preserves alpha ownership and direct/retained output.</summary>
    /// <returns>The hardware movement oracle.</returns>
    [MapGpuFact]
    public async Task DeterministicPanZoomPathMatchesRetainedAnalyticWaterAsync()
    {
        using var device = VulkanDevice.Create();
        await using var memory = new MemoryManager();
        await using var manager = new ResourceManager("movement", new Source(), [new Stage()], memory, new Runtime());
        var style = new MapStyle("visual", "v1", new(0, 0, 0, 1), [new("water", new(0, 0, 1, 0.5f))]);
        await using var map = new MapSession(manager, new Tiles(), style, new() { SelectVisibleTiles = true }, [new(1, 0, 0), new(1, 1, 0)]);
        map.SetCamera(new(0, 66.51326044311186, 1));
        using var surface = new UiSurface(new MapView { Session = map });
        surface.SetViewport(new(512, 128));
        surface.UpdateLayout();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (map.PublishedTiles != 2)
        {
            map.Advance();
            await Task.Delay(1, deadline.Token);
        }

        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        foreach (int step in Enumerable.Range(0, 4))
        {
            if (step == 1)
            {
                surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(100, 50)));
                surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(100.5f, 50)));
                surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(100.5f, 50)));
            }
            else if (step == 2)
            {
                surface.Dispatch(new UiWheelInput(new(300, 32), new(0, -64)));
            }
            else if (step == 3)
            {
                map.Pan(new(-20, 3));
            }

            var canvas = new Canvas2D();
            canvas.Begin();
            surface.Record(canvas);
            byte[] direct = await RenderAsync(device, canvas.End(), null, 1);
            scene.Update();
            using DrawingSceneReadLease2D borrow = scene.Borrow();
            Assert.Equal(direct, await RenderAsync(device, null, borrow, 1));
            for (int x = 16; x < 496; x++)
            {
                Pixel(direct, 1, x, 32, 0, 0, 188);
            }
        }
    }

    private static void Pixel(byte[] pixels, float dpi, int x, int y, byte red, byte green, byte blue)
    {
        int offset = (((int)(y * dpi) * (int)(512 * dpi)) + (int)(x * dpi)) * 4;
        Assert.InRange(pixels[offset], Math.Max(0, red - 1), Math.Min(255, red + 1));
        Assert.InRange(pixels[offset + 1], Math.Max(0, green - 1), Math.Min(255, green + 1));
        Assert.InRange(pixels[offset + 2], Math.Max(0, blue - 1), Math.Min(255, blue + 1));
        Assert.Equal(255, pixels[offset + 3]);
    }

    private static async Task<byte[]> RenderAsync(IGraphicDevice device, DrawList2D? direct, DrawingSceneReadLease2D? retained, float dpi)
    {
        uint width = (uint)(512 * dpi);
        uint height = (uint)(128 * dpi);
        using IGraphicsTexture target = device.CreateTexture(new() { Width = width, Height = height, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        var output = new RenderTarget2D { View = view, PixelScale = dpi, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(1, 0, 1, 1) };
        using PreparedDraw2D? immediate = direct is null ? null : await renderer.PrepareAsync(direct, output);
        using PreparedDrawingFrame2D? frame = retained is null ? null : await renderer.PrepareAsync(retained, output);
        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment;
        uint pitch = (((width * 4) + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * height, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), Before = default, After = color, BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment });
        if (immediate is not null)
        {
            immediate.Record(commands);
        }
        else
        {
            frame!.Record(commands);
        }

        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), Before = color, After = copyRead, BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = width, Height = height }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = height });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        frame?.AttachSubmission(submission);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] padded = new byte[readback.Count];
        readback.CopyTo(padded);
        readback.Unmap();
        commands.Dispose();
        frame?.Retire();
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            padded.AsSpan((int)(y * pitch), (int)(width * 4)).CopyTo(pixels.AsSpan((int)(y * width * 4)));
        }

        return pixels;
    }

    private sealed class OverflowWidget : Compose.Definitions.Widget
    {
        protected override UiElement CreateElement() => new OverflowElement(this);
    }

    private sealed class OverflowElement(OverflowWidget widget) : UiElement<OverflowWidget>(widget)
    {
        protected override bool CacheRetainedDrawing => true;

        protected override System.Numerics.Vector2 LayoutOverride(UiBoxConstraints constraints) => default;

        protected override void OnRecord(DrawingCanvas2D canvas) => canvas.FillRect(new(0, 112, 512, 8), new SRgba<float>(1, 1, 0, 1));
    }

    private sealed class Tiles() : MapTileSet("visual", "v1", "visual", 1, 2, "handmade", new("visual"))
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => new($"visual/{id.X}");
    }

    private sealed class Stage : IResourceSourceStage<MapTileContent>
    {
        public ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            int extent = context.Address.Id.Value!.EndsWith('0') ? 1024 : 8192;
            MapTileFeature area = new(1, MapGeometryType.Polygon, [new([new(-extent, -extent), new(2 * extent, -extent), new(2 * extent, 2 * extent), new(-extent, 2 * extent)], false)], []);
            MapTileFeature road = new(2, MapGeometryType.LineString, [new([new(-extent, extent / 2), new(2 * extent, extent / 2)], false)], []);
            return ValueTask.FromResult(new MapTileContent([new("water", (uint)extent, [area]), new("roads", (uint)extent, [road])], "v1", 2048, 4096));
        }

        public ValueTask ReleaseAsync(MapTileContent resource) => ValueTask.CompletedTask;
    }

    private sealed class Source : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("v1");
    }

    private sealed class Runtime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
