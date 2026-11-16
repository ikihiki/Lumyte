using System.Security.Cryptography;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Maps.Mvt;
using Lumyte.Memory;
using Lumyte.Resources;
using Xunit;

namespace Lumyte.Maps.Session.Tests;

/// <summary>Opt-in full cached ODbL fixture verification, without networking or reduced geometry.</summary>
public sealed class RealOsmQualificationTests
{
    /// <summary>Checks full-tile direct/retained output through scripted camera and DPI changes.</summary>
    /// <returns>The hardware verification.</returns>
    [RealMvtFact]
    public async Task CachedFullShinjukuAsync()
    {
        string? fixture = Environment.GetEnvironmentVariable("LUMYTE_REAL_MVT");
        Assert.NotNull(fixture);
        byte[] bytes = File.ReadAllBytes(fixture);
        Assert.Equal("58961181bac9bbcaa5308501bff61a81ae775a04326c3a685c04e3c631014ba0", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var tile = new MapTileId(16, 58199, 25804);
        var profile = new MapTileLoadProfile("real-osm-default");
        var registration = new InMemoryMvtRegistration("real-shinjuku", "1", profile, new Dictionary<MapTileId, ReadOnlyMemory<byte>> { [tile] = bytes });
        await using var memory = new MemoryManager();
        await using var resources = new ResourceManager("real-shinjuku", new InMemoryMvtSource(registration), [new MvtResourceStage(registration)], memory, new Runtime());
        var style = new MapStyle(MvtDecoder.SchemaId, "1", new(0, 0, 0, 1), [new("water", new(0, 0, 1, 1)), new("buildings", new(1, 0, 0, 1)), new("roads", new(1, 1, 1, 1), new StrokeStyle2D { Width = 2 })]);
        await using var map = new MapSession(resources, new Tiles(registration), style, new(), [tile]);
        double lon = ((58199.5 / 65536) * 360) - 180;
        double lat = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * 25804.5 / 65536)))) * 180 / Math.PI;
        map.SetViewport(new(512, 512));
        map.SetCamera(new(lon, lat, 16));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (map.PublishedTiles != 1)
        {
            map.Advance();
            if (map.Failures.Count != 0)
            {
                throw map.Failures[0];
            }

            await Task.Delay(1, deadline.Token);
        }

        using var device = VulkanDevice.Create();
        string output = Environment.GetEnvironmentVariable("LUMYTE_REAL_OUTPUT") ?? Path.GetTempPath();
        Directory.CreateDirectory(output);
        Assert.Equal(1518, MvtDecoder.Decode(bytes, profile, "cached-shinjuku").Layers.Sum(layer => layer.Features.Length));
        foreach (int capacity in new[] { 1, 3 })
        {
            using IRenderer2D legacy = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxFramesInFlight = capacity });
            using IRenderer2D standard = Renderer2D.CreateDefault(device, retentionOptions: new() { MaxFramesInFlight = capacity });
            foreach ((string Name, float Dpi, double Pan, double Zoom, uint Width) scenario in new[] { ("static", 1f, 0.0, 16.0, 512u), ("pan", 1.5f, 0.0002, 16.0, 512u), ("zoom", 2f, 0.0, 16.2, 512u), ("resize", 1f, 0.0, 16.0, 576u) })
            {
                map.SetCamera(new(lon + scenario.Pan, lat, scenario.Zoom));
                map.SetViewport(new(scenario.Width, 512));
                var canvas = new Canvas2D();
                canvas.Begin();
                map.Record(canvas);
                DrawList2D drawing = canvas.End();
                Assert.True(drawing.Count > 1000);
                using var scene = new DrawingScene2D();
                scene.Insert(scene.Root, 0, drawing);
                using DrawingSceneReadLease2D borrow = scene.Borrow();
                foreach ((string Name, IRenderer2D Renderer) variant in new[] { ("Legacy", legacy), ("Standard", standard) })
                {
                    byte[] direct = await RenderAsync(device, drawing, variant.Renderer, scenario.Dpi, scenario.Width, null);
                    byte[] retained = await RenderAsync(device, drawing, variant.Renderer, scenario.Dpi, scenario.Width, borrow);
                    Assert.Equal(direct, retained);
                    byte[] reused = await RenderAsync(device, drawing, variant.Renderer, scenario.Dpi, scenario.Width, borrow);
                    Assert.Equal(direct, reused);
                    Assert.True(variant.Renderer.RetentionStatistics.ReusedRuns > 0);
                    File.WriteAllBytes(Path.Combine(output, $"{capacity}-{scenario.Name}-{variant.Name}.rgba"), direct);
                }
            }
        }

        await map.DisposeAsync();
        Assert.Equal(0, map.ResidentCpuBytes);
    }

    private static async Task<byte[]> RenderAsync(IGraphicDevice device, DrawList2D direct, IRenderer2D renderer, float dpi, uint logicalWidth, DrawingSceneReadLease2D? retained)
    {
        uint width = (uint)(logicalWidth * dpi);
        uint height = (uint)(512 * dpi);
        using IGraphicsTexture target = device.CreateTexture(new() { Width = width, Height = height, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        var output = new RenderTarget2D { View = view, PixelScale = dpi, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(1, 0, 1, 1) };
        using PreparedDraw2D? immediate = retained is not null ? null : await renderer.PrepareAsync(direct, output);
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

    private sealed class RealMvtFactAttribute : FactAttribute
    {
        public RealMvtFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LUMYTE_REAL_MVT") is null || Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
            {
                Skip = "Set LUMYTE_REAL_MVT and LUMYTE_GRAPHICS_GPU_TESTS=1 for the cached full-tile hardware fixture.";
            }
        }
    }

    private sealed class Tiles(InMemoryMvtRegistration registration) : MapTileSet("real-shinjuku", "1", MvtDecoder.SchemaId, 0, 22, "© OpenStreetMap contributors; ODbL https://www.openstreetmap.org/copyright https://opendatacommons.org/licenses/odbl/1-0/", registration.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);
    }

    private sealed class Runtime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
