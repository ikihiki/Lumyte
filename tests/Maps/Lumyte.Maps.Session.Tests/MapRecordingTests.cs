using System.Collections.Immutable;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Memory;
using Lumyte.Resources;
using Xunit;

namespace Lumyte.Maps.Session.Tests;

/// <summary>Fixed-set world repetition and shared record/preflight eligibility regressions.</summary>
public sealed class MapRecordingTests
{
    /// <summary>Finite known IDs are selected before I/O, with exact viewport boundaries and integer source zoom.</summary>
    /// <returns>The deterministic movement and acquisition checks.</returns>
    [Fact]
    public async Task VisibleFiniteUniverseSelectsSourceZoomAndSharesRepeatedWorldsAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage(false);
        await using var manager = new ResourceManager("visible", new Source(), [stage], memory, new Runtime());
        MapTileId[] universe = [new(1, 0, 0), new(1, 1, 0), new(2, 0, 0), new(2, 3, 0)];
        await using var map = new MapSession(manager, new Tiles(1, 2), Style(false), new() { SelectVisibleTiles = true }, universe);
        map.SetViewport(new(512, 128));
        map.SetCamera(new(-90, 66.51326044311186, 1));
        Assert.Equal([new MapTileId(1, 0, 0)], map.VisibleTiles);
        Assert.Equal(0, stage.Builds);
        await AdoptCountAsync(map, 1);
        Assert.Equal(1, stage.Builds);
        map.SetCamera(new(179, 66.51326044311186, 1.9));
        Assert.Equal(1, map.SourceZoom);
        Assert.Equal(universe.Take(2), map.VisibleTiles);
        await AdoptCountAsync(map, 2);
        map.SetCamera(new(-179, 66.51326044311186, 1.9));
        Assert.Equal(universe.Take(2), map.VisibleTiles);
        map.SetViewport(new(3000, 128));
        Assert.Equal(2, map.VisibleTiles.Count);
        map.Advance();
        Assert.Equal(2, stage.Builds);
        map.SetViewport(new(128, 128));
        map.SetCamera(new(-135, 80, 2));
        Assert.Equal(2, map.SourceZoom);
        Assert.Equal([new MapTileId(2, 0, 0)], map.VisibleTiles);
        await AdoptCountAsync(map, 3);
        Assert.Equal(2, Record(map).Count);
        map.SetCamera(new(0, 80, 2));
        Assert.Empty(map.VisibleTiles);
        Assert.Equal(1, Record(map).Count);
        map.SetViewport(default);
        map.SetCamera(new(135, 80, 2));
        map.Advance();
        Assert.Empty(map.VisibleTiles);
        Assert.Equal(3, stage.Builds);
        map.SetViewport(new(128, 128));
        await AdoptCountAsync(map, 4);
        Assert.Equal(4, stage.Builds);
        Assert.Equal(2, Record(map).Count);
        map.SetCamera(new(-135, 80, 2));
        map.Advance();
        Assert.Equal(4, stage.Builds);
    }

    /// <summary>Both already-loaded sides remain visible near either dateline without new demand.</summary>
    /// <returns>The retained fixed-set checks.</returns>
    [Fact]
    public async Task FixedLoadedTilesRepeatAtBothDatelineSidesAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage(false);
        await using var manager = new ResourceManager("world-wrap", new Source(), [stage], memory, new Runtime());
        await using var map = new MapSession(manager, new Tiles(1), Style(false), new(), [new(1, 0, 0), new(1, 1, 0)]);
        map.SetViewport(new(128, 128));
        map.SetCamera(new(0, 66.51326044311186, 1));
        await AdoptAsync(map);
        foreach (double longitude in new[] { 179.0, -179.0 })
        {
            map.SetCamera(new(longitude, 66.51326044311186, 1));
            Assert.Equal(3, Record(map).Count);
            Assert.Equal(2, stage.Builds);
            Assert.Equal(2, map.PublishedTiles);
        }

        map.SetViewport(new(2100, 128));
        Assert.True(Record(map).Count >= 5);
        Assert.Equal(2, stage.Builds);
    }

    /// <summary>Offscreen tiles and geometrically ineligible paint features do not consume path conversion budget.</summary>
    /// <returns>The tight-budget rendering checks.</returns>
    [Fact]
    public async Task DenseOffscreenAndPaintIneligibleFeaturesDoNotRejectSmallVisibleMapAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage(true);
        await using var manager = new ResourceManager("record-budget", new Source(), [stage], memory, new Runtime());
        await using var map = new MapSession(manager, new Tiles(2), Style(true), new() { MaxRecordingCpuBytes = 4096 }, [new(2, 2, 1), new(2, 2, 3)]);
        map.SetViewport(new(128, 128));
        map.SetCamera(new(45, 40, 2));
        await AdoptAsync(map);
        Assert.Equal(2, Record(map).Count);
        map.SetCamera(new(45, -80, 2));
        Assert.Throws<InvalidOperationException>(() => Record(map));
        map.SetViewport(default);
        Assert.Equal(0, Record(map).Count);
    }

    private static async Task AdoptCountAsync(MapSession map, int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (map.PublishedTiles != count)
        {
            map.Advance();
            await Task.Delay(1, deadline.Token);
        }
    }

    private static DrawList2D Record(MapSession map)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        map.Record(canvas);
        return canvas.End();
    }

    private static MapStyle Style(bool roads)
    {
        MapStyleLayer[] layers = roads
            ? [new("water", new(0, 0, 1, 1)), new("roads", new(1, 0, 0, 1), new StrokeStyle2D { Width = 2 })]
            : [new("water", new(0, 0, 1, 1))];
        return new("recording", "v1", new(0, 0, 0, 1), layers);
    }

    private static async Task AdoptAsync(MapSession map)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (map.PublishedTiles != 2)
        {
            map.Advance();
            await Task.Delay(1, deadline.Token);
        }
    }

    private sealed class Tiles(int zoom, int? maximum = null) : MapTileSet("recording", "v1", "recording", zoom, maximum ?? zoom, "handmade", new("recording"))
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => new($"recording/{id.Zoom}/{id.X}/{id.Y}");
    }

    private sealed class Stage(bool dense) : IResourceSourceStage<MapTileContent>
    {
        internal int Builds { get; private set; }

        public ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            var polygon = new MapTileFeature(1, MapGeometryType.Polygon, [new([new(-1024, -1024), new(2048, -1024), new(2048, 2048), new(-1024, 2048)], false)], []);
            bool offscreen = dense && context.Address.Id.Value!.EndsWith('3');
            ImmutableArray<MapTileFeature> water = offscreen ? Enumerable.Repeat(polygon, 1000).ToImmutableArray() : [polygon];
            ImmutableArray<MapTileFeature> ineligible = dense ? Enumerable.Repeat(polygon, 1000).ToImmutableArray() : [];
            return ValueTask.FromResult(new MapTileContent([new("water", 1024, water), new("roads", 1024, ineligible)], "v1", 65536, 65536));
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
